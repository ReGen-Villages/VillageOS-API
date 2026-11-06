import asyncio
import os
import pickle
import time
from concurrent.futures import ThreadPoolExecutor

import pytest

from quadrat import Refusal
from quadrat.pool import ReadPool


def process_id():
    return os.getpid()


def refuse():
    raise Refusal("outside_raster", "the point lies outside the raster")


def die():
    os._exit(1)


def add(first, second):
    return first + second


def test_a_refusal_survives_being_sent_between_processes():
    sent = pickle.loads(pickle.dumps(Refusal("too_many_cells", "that is a lot of cells")))
    assert (sent.code, sent.reason) == ("too_many_cells", "that is a lot of cells")
    assert str(sent) == "too_many_cells: that is a lot of cells"


def test_a_read_runs_in_a_worker_process_not_in_this_one():
    pool = ReadPool(workers=1)
    try:
        assert asyncio.run(pool.run(process_id)) != os.getpid()
    finally:
        pool.close()


def test_the_arguments_and_the_answer_cross_the_process_boundary():
    pool = ReadPool(workers=1)
    try:
        assert asyncio.run(pool.run(add, 2, 3)) == 5
    finally:
        pool.close()


def test_a_refusal_raised_in_a_worker_arrives_with_its_code():
    pool = ReadPool(workers=1)
    try:
        with pytest.raises(Refusal) as refusal:
            asyncio.run(pool.run(refuse))
        assert refusal.value.code == "outside_raster"
    finally:
        pool.close()


def test_a_worker_that_dies_is_a_refusal_the_caller_can_resend_and_the_pool_carries_on():
    pool = ReadPool(workers=1)
    try:
        async def go():
            with pytest.raises(Refusal) as refusal:
                await pool.run(die)
            return refusal.value.code, await pool.run(add, 1, 1)

        assert asyncio.run(go()) == ("read_failed", 2)
    finally:
        pool.close()


def test_no_more_reads_go_at_once_than_there_are_workers():
    pool = ReadPool(workers=2, executor_factory=lambda: ThreadPoolExecutor(2))
    seen = {"running": 0, "peak": 0}

    def work():
        seen["running"] += 1
        seen["peak"] = max(seen["peak"], seen["running"])
        time.sleep(0.05)
        seen["running"] -= 1

    async def go():
        await asyncio.gather(*[pool.run(work) for _ in range(6)])

    asyncio.run(go())
    pool.close()
    assert seen["peak"] <= 2


def test_a_pool_made_with_another_kind_of_executor_runs_in_it():
    pool = ReadPool(workers=1, executor_factory=lambda: ThreadPoolExecutor(1))
    assert asyncio.run(pool.run(process_id)) == os.getpid()
    pool.close()
