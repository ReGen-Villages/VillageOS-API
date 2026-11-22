"""Quadrat's answer recorded on a Thing as observations, through Mycelium's batch route, when a request asks for it."""
from unittest.mock import AsyncMock

import pytest

import app as appmod
from app import host

pytestmark = pytest.mark.usefixtures("reads_in_this_process", "readable")

RUN = {"runId": "6d2b0c1e-0000-4000-8000-000000000001", "nodeId": "6d2b0c1e-0000-4000-8000-000000000002"}
THING = "6d2b0c1e-0000-4000-8000-0000000000aa"


@pytest.fixture
def recording(monkeypatch):
    record = AsyncMock(return_value=2)
    monkeypatch.setattr(host.mycelium, "record_observations", record)
    return record


def call(client, inputs, params=None, rehearsal=False):
    body = {**RUN, "params": {"operation": "zonal", **(params or {})}, "inputs": inputs}
    if rehearsal:
        body["rehearsal"] = True
    return client.post("/handle", json=body).json()


def zone(degrees_grid, **extra):
    return {"raster": degrees_grid, "bbox": [0, 8, 2, 10], **extra}


def test_the_statistics_asked_for_are_recorded_on_the_thing_in_one_batch(client, degrees_grid, recording):
    body = call(client, zone(degrees_grid, thingId=THING, observations={"elevation_mean_m": "mean", "elevation_max_m": "max"}))
    assert body["result"] == "success"
    recording.assert_awaited_once()
    thing, samples = recording.await_args.args
    assert thing == THING
    assert samples == [{"property": "elevation_mean_m", "value": 5.5}, {"property": "elevation_max_m", "value": 11.0}]
    assert body["outputs"]["written"] == {"thingId": THING, "accepted": 2, "properties": ["elevation_mean_m", "elevation_max_m"], "skipped": []}
    assert body["outputs"]["result"]["mean"] == 5.5


def test_a_request_that_asks_for_nothing_is_recorded_nowhere(client, degrees_grid, recording):
    body = call(client, zone(degrees_grid))
    assert body["result"] == "success" and body["outputs"]["written"] == {"accepted": 0}
    recording.assert_not_awaited()


def test_a_rehearsal_answers_what_it_would_record_and_records_nothing(client, degrees_grid, recording):
    body = call(client, zone(degrees_grid, thingId=THING, observations={"m": "mean"}), rehearsal=True)
    assert body["result"] == "success"
    assert body["outputs"]["written"] == {"thingId": THING, "accepted": 0, "properties": ["m"], "skipped": [], "rehearsal": True}
    recording.assert_not_awaited()


def test_a_refused_write_fails_the_node_with_the_platforms_words(client, degrees_grid, recording):
    recording.side_effect = RuntimeError("observation batch returned 405")
    body = call(client, zone(degrees_grid, thingId=THING, observations={"m": "mean"}))
    assert body["result"] == "fail" and body["outputs"] == {}
    assert body["error"].startswith("write_failed:") and "405" in body["error"]


def test_a_request_that_asks_wrongly_is_refused_before_anything_is_read(client, recording):
    body = call(client, {"raster": "no-such-file.tif", "bbox": [0, 0, 1, 1], "thingId": "nope", "observations": {"m": "mean"}})
    assert body["error"].startswith("invalid_thing:")
    recording.assert_not_awaited()


def test_a_statistic_the_answer_lacks_is_refused_and_nothing_is_recorded(client, degrees_grid, recording):
    body = call(client, zone(degrees_grid, thingId=THING, observations={"m": "no_such_statistic"}))
    assert body["error"].startswith("unknown_statistic:")
    recording.assert_not_awaited()


def test_a_statistic_that_is_null_is_left_out_and_said_so(client, degrees_grid, recording):
    recording.return_value = 1
    body = call(client, {"raster": degrees_grid, "lon": 9.5, "lat": 0.5, "thingId": THING, "observations": {"v": "value"}}, {"operation": "point"})
    assert body["result"] == "success"
    assert body["outputs"]["written"]["skipped"] == ["v"] and body["outputs"]["written"]["accepted"] == 0
    recording.assert_not_awaited()
