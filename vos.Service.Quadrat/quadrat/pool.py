import asyncio
import atexit
import multiprocessing
import os
import threading
from concurrent.futures import ProcessPoolExecutor
from concurrent.futures.process import BrokenProcessPool

from .refusal import Refusal


def end_with_parent():
    """Run in each worker as it starts: the worker ends when the process that started it does.

    A worker is not stopped by its parent being killed outright (an operator, Mycelium stopping a daemon, a crash):
    it keeps waiting for work, and holds the memory a read had taken, until the machine restarts.
    """
    parent = multiprocessing.parent_process()
    if parent is None:
        return

    def watch():
        parent.join()
        os._exit(0)

    threading.Thread(target=watch, daemon=True, name="end-with-parent").start()


class ReadPool:
    """Runs reads in worker processes.

    The raster library holds Python's global interpreter lock for most of a large read, so a read run in a thread
    stalls everything else in the service, `/health` among it. A worker process has a lock of its own. The number of
    workers is the number of reads that can go at once, and a worker the raster library brings down takes only
    itself with it.
    """

    def __init__(self, workers, executor_factory=None):
        self._workers = workers
        self._factory = executor_factory or self._processes
        self._executor = None
        atexit.register(self.close)

    def _processes(self):
        # Spawned, not forked: the raster library is not safe to fork once the service has threads running.
        return ProcessPoolExecutor(max_workers=self._workers, mp_context=multiprocessing.get_context("spawn"), initializer=end_with_parent)

    async def run(self, function, *args):
        if self._executor is None:
            self._executor = self._factory()
        executor = self._executor
        try:
            return await asyncio.get_running_loop().run_in_executor(executor, function, *args)
        except BrokenProcessPool as broken:
            if self._executor is executor:
                self._executor = None
            executor.shutdown(wait=False, cancel_futures=True)
            raise Refusal("read_failed", "the process reading the raster stopped unexpectedly; the request can be sent again") from broken

    def close(self):
        executor, self._executor = self._executor, None
        if executor is not None:
            executor.shutdown(wait=False, cancel_futures=True)
