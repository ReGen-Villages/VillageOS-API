import asyncio
import atexit
import multiprocessing
from concurrent.futures import ProcessPoolExecutor
from concurrent.futures.process import BrokenProcessPool

from .refusal import Refusal


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
        return ProcessPoolExecutor(max_workers=self._workers, mp_context=multiprocessing.get_context("spawn"))

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
