"""A stand-in for the service, run as its own process so a test can kill it the way an operator or Mycelium would.

It starts a pool, runs one read that answers the worker's process id, prints that id, and then waits to be killed.
"""
import asyncio
import os
import sys
import time
from pathlib import Path

# Run as a script, Python looks for modules next to the script, not in the folder it was started from.
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from quadrat.pool import ReadPool  # noqa: E402


def worker_process_id():
    return os.getpid()


if __name__ == "__main__":
    pool = ReadPool(workers=1)
    worker = asyncio.run(pool.run(worker_process_id))
    print(f"ready {worker}", flush=True)
    time.sleep(600)
