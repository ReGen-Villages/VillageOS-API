"""A geometry reaches Quadrat in any of the forms the platform holds one in: the object, its text, or a GeoJson value."""
import json

import pytest

from conftest import square
from quadrat import Refusal
from quadrat.geojson import geometry_of

pytestmark = pytest.mark.usefixtures("reads_in_this_process", "readable")

RUN = {"runId": "6d2b0c1e-0000-4000-8000-000000000001", "nodeId": "6d2b0c1e-0000-4000-8000-000000000002"}
POLYGON = square(0, 8, 2, 10)


def refused(value):
    with pytest.raises(Refusal) as raised:
        geometry_of(value)
    return raised.value.code


def test_a_geometry_given_as_the_object_is_read_as_it_is():
    assert geometry_of(POLYGON) == POLYGON


def test_a_geometry_given_as_its_text_is_read():
    assert geometry_of(json.dumps(POLYGON)) == POLYGON


def test_a_geojson_value_the_platform_holds_is_read_through_its_text():
    assert geometry_of({"Json": json.dumps(POLYGON)}) == POLYGON


def test_a_geojson_value_that_holds_the_object_is_read_too():
    assert geometry_of({"Json": POLYGON}) == POLYGON


def test_nothing_given_is_left_for_the_caller_to_refuse_as_missing():
    assert geometry_of(None) is None


@pytest.mark.parametrize("value", ["not json", "[1, 2]", "42", {"Json": "not json"}, {"Json": 3}, {"other": 1}, 7, True])
def test_anything_else_is_refused_as_an_invalid_geometry(value):
    assert refused(value) == "invalid_geometry"


def call(client, operation, inputs):
    return client.post("/handle", json={**RUN, "params": {"operation": operation}, "inputs": inputs}).json()


def test_a_zone_held_as_a_geojson_value_is_summarised(client, degrees_grid):
    body = call(client, "zonal", {"raster": degrees_grid, "geometry": {"Json": json.dumps(POLYGON)}})
    assert body["result"] == "success", body
    assert body["outputs"]["result"]["count"] == 4.0


def test_a_zone_held_as_text_is_summarised(client, degrees_grid):
    body = call(client, "zonal", {"raster": degrees_grid, "geometry": json.dumps(POLYGON)})
    assert body["result"] == "success", body
    assert body["outputs"]["result"]["mean"] == 5.5


def test_a_line_held_as_a_geojson_value_is_read(client, degrees_grid):
    line = {"type": "LineString", "coordinates": [[0.5, 7.5], [4.5, 7.5]]}
    body = call(client, "profile", {"raster": degrees_grid, "line": {"Json": json.dumps(line)}, "samples": 3})
    assert body["result"] == "success", body
    assert [s["value"] for s in body["outputs"]["result"]["samples"]] == [20.0, 22.0, 24.0]


def test_a_geometry_that_is_not_one_is_refused_with_its_code(client, degrees_grid):
    body = call(client, "zonal", {"raster": degrees_grid, "geometry": "not json"})
    assert body["result"] == "fail" and body["error"].startswith("invalid_geometry:")
