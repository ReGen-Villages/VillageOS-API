"""A stand-in for the service, run as its own process so a test can kill it the way an operator or Mycelium would.

It starts a pool, runs one read whose worker writes a heartbeat file, and then waits to be killed.
"""
import asyncio
import os
import sys
import threading
import time
from pathlib import Path

# Run as a script, Python looks for modules next to the script, not in the folder it was started from.
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from quadrat.pool import ReadPool  # noqa: E402


def start_heartbeat(path):
    def beat():
        while True:
            with open(path, "w") as file:
                file.write(str(time.time()))
            time.sleep(0.1)

    threading.Thread(target=beat, daemon=True).start()
    return os.getpid()


if __name__ == "__main__":
    pool = ReadPool(workers=1)
    worker = asyncio.run(pool.run(start_heartbeat, sys.argv[1]))
    print(f"ready {worker}", flush=True)
    time.sleep(600)
