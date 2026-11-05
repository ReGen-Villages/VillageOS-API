"""The raster reader as a pipeline node: the envelope in, the outputs or the refusal out.

The contract itself (the endpoints, inbound JWT validation, registration, arguments) is tested in
vos.Service.Python.Shared. This covers what Quadrat adds: the node envelope, its operations and its
manifest. Registration is mocked so the tests never touch the network.
"""

import base64
import time
from unittest.mock import AsyncMock, patch

import jwt
import pytest
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.hazmat.primitives.serialization import Encoding, PublicFormat
from fastapi.testclient import TestClient

import app as appmod
from app import app, host
from conftest import write_png
from quadrat.sources import Sources

THIS_HANDLER = "quadrat-handler"
RUN = {"runId": "6d2b0c1e-0000-4000-8000-000000000001", "nodeId": "6d2b0c1e-0000-4000-8000-000000000002"}


@pytest.fixture(autouse=True)
def readable(tmp_path, monkeypatch):
    """The service reads only what it is configured with; these tests configure their own temporary folder."""
    monkeypatch.setattr(appmod, "sources", Sources(directories=[str(tmp_path)]))


@pytest.fixture
def client():
    with patch.object(host.mycelium, "register", new=AsyncMock(return_value=True)):
        with TestClient(app) as c:
            yield c


@pytest.fixture
def signing(monkeypatch):
    """Enable JWT auth against a stand-in for Mycelium's key pair; yields the private half."""
    pair = ec.generate_private_key(ec.SECP256R1())
    verification_key = base64.b64encode(
        pair.public_key().public_bytes(Encoding.DER, PublicFormat.SubjectPublicKeyInfo)
    ).decode()
    monkeypatch.setattr(appmod.config, "verification_key", verification_key)
    monkeypatch.setattr(appmod.config, "issuer", "VillageOS")
    monkeypatch.setattr(appmod.config, "audience", THIS_HANDLER)
    return pair


def make_token(pair) -> str:
    now = int(time.time())
    claims = {"iss": "VillageOS", "aud": THIS_HANDLER, "sub": "mycelium", "iat": now, "nbf": now, "exp": now + 60}
    return jwt.encode(claims, pair, algorithm="ES256")


def phloem_reads(body):
    """The answer as Phloem's PipelineExecutor.ParseNodeResponse reads it: `result` must be the string
    success or fail, and a fail carries its error. An answer that says `success: true` fails every node."""
    result = body.get("result") if isinstance(body, dict) else None
    assert result in ("success", "fail"), f"Phloem fails an answer whose result is {result!r}: {body}"
    if result == "fail":
        assert isinstance(body.get("error"), str) and body["error"], "a fail answer says why"
    return result


def run_node(client, params, inputs):
    res = client.post("/handle", json={**RUN, "params": params, "inputs": inputs})
    if res.status_code == 200:
        phloem_reads(res.json())
    return res


def test_health_names_the_service(client):
    res = client.get("/health")
    assert res.status_code == 200
    assert res.json()["service"] == "Quadrat"


def test_metadata_answers_the_rasters_shape(client, degrees_grid):
    res = run_node(client, {"operation": "metadata"}, {"raster": degrees_grid})
    body = res.json()
    assert res.status_code == 200
    assert body["result"] == "success"
    assert body["outputs"]["result"]["width"] == 10


def test_point_answers_the_value_at_a_coordinate(client, degrees_grid):
    res = run_node(client, {"operation": "point"}, {"raster": degrees_grid, "lon": 2.5, "lat": 7.5})
    assert res.json()["outputs"]["result"]["value"] == 22.0


def test_point_takes_its_resampling_from_the_params(client, degrees_grid):
    res = run_node(client, {"operation": "point", "resampling": "bilinear"}, {"raster": degrees_grid, "lon": 3.0, "lat": 7.5})
    assert res.json()["outputs"]["result"]["value"] == pytest.approx(22.5)


def test_zonal_over_a_box(client, degrees_grid):
    res = run_node(client, {"operation": "zonal"}, {"raster": degrees_grid, "bbox": [0, 8, 2, 10]})
    result = res.json()["outputs"]["result"]
    assert result["count"] == 4.0
    assert result["mean"] == 5.5


def test_zonal_over_a_polygon(client, degrees_grid):
    polygon = {"type": "Polygon", "coordinates": [[[0, 8], [2, 8], [2, 10], [0, 10], [0, 8]]]}
    res = run_node(client, {"operation": "zonal"}, {"raster": degrees_grid, "geometry": polygon})
    assert res.json()["outputs"]["result"]["count"] == 4.0


def test_a_zone_given_two_ways_is_refused(client, degrees_grid):
    polygon = {"type": "Polygon", "coordinates": [[[0, 8], [2, 8], [2, 10], [0, 10], [0, 8]]]}
    res = run_node(client, {"operation": "zonal"}, {"raster": degrees_grid, "bbox": [0, 8, 2, 10], "geometry": polygon})
    body = res.json()
    assert body["result"] == "fail"
    assert "bbox" in body["error"] and "geometry" in body["error"]


def test_a_request_quadrat_will_not_answer_is_reported_with_its_code(client, degrees_grid):
    res = run_node(client, {"operation": "point"}, {"raster": degrees_grid, "lon": 50, "lat": 50})
    body = res.json()
    assert res.status_code == 200
    assert body["result"] == "fail"
    assert body["outputs"] == {}
    assert body["error"].startswith("outside_raster:")


def test_an_unreadable_raster_is_reported_not_raised(client, tmp_path):
    res = run_node(client, {"operation": "metadata"}, {"raster": str(tmp_path / "missing.tif")})
    body = res.json()
    assert res.status_code == 200
    assert body["result"] == "fail"
    assert body["error"].startswith("unreadable_raster:")


@pytest.mark.parametrize("params", [{}, {"operation": "histogram"}])
def test_a_missing_or_unknown_operation_names_the_ones_there_are(client, degrees_grid, params):
    body = run_node(client, params, {"raster": degrees_grid}).json()
    assert body["result"] == "fail"
    assert "metadata" in body["error"] and "point" in body["error"] and "zonal" in body["error"]


@pytest.mark.parametrize("inputs, missing", [
    ({}, "raster"),
    ({"raster": "RASTER", "lat": 1.0}, "lon"),
    ({"raster": "RASTER", "lon": 1.0}, "lat"),
    ({"raster": "RASTER"}, "bbox"),
])
def test_an_operation_missing_an_input_says_which(client, degrees_grid, inputs, missing):
    operation = "zonal" if missing == "bbox" else "point" if missing in ("lon", "lat") else "metadata"
    inputs = {name: degrees_grid if value == "RASTER" else value for name, value in inputs.items()}
    body = run_node(client, {"operation": operation}, inputs).json()
    assert body["result"] == "fail"
    assert missing in body["error"]


def test_a_body_that_is_not_a_node_envelope_is_refused_with_what_is_expected(client):
    res = client.post("/handle", json={"relationshipId": "r", "subjectId": "s", "targetId": "t", "properties": {}})
    assert res.status_code == 400
    assert "runId" in res.json()["error"] and "nodeId" in res.json()["error"]


def test_the_manifest_declares_the_ports(client):
    ports = client.get("/manifest").json()["ports"]
    inputs = {p["portName"]: p for p in ports if p["direction"] == "in"}
    outputs = [p["portName"] for p in ports if p["direction"] == "out"]
    assert inputs["raster"]["required"] is True
    assert {"lon", "lat", "bbox", "geometry"} <= set(inputs)
    assert outputs == ["result"]


def test_handle_requires_a_token_once_a_key_is_configured(client, signing, degrees_grid):
    body = {**RUN, "params": {"operation": "metadata"}, "inputs": {"raster": degrees_grid}}
    assert client.post("/handle", json=body).status_code == 401
    ok = client.post("/handle", json=body, headers={"Authorization": f"Bearer {make_token(signing)}"})
    assert ok.status_code == 200
    assert ok.json()["result"] == "success"


@pytest.fixture
def terrain_png(tmp_path):
    return write_png(tmp_path / "tile.png", [[(135, 170, 128), (128, 10, 0)], [(128, 20, 0), (128, 30, 0)]])


def test_a_point_is_read_from_a_terrain_tile(client, terrain_png):
    res = run_node(client, {"operation": "point", "encoding": "terrarium"}, {"raster": terrain_png, "tile": {"z": 1, "x": 1, "y": 0}, "lon": 10, "lat": 80})
    assert res.json()["outputs"]["result"]["value"] == 1962.5


def test_a_zone_is_read_from_a_terrain_tile(client, terrain_png):
    res = run_node(client, {"operation": "zonal", "encoding": "terrarium"}, {"raster": terrain_png, "tile": {"z": 1, "x": 1, "y": 0}, "bbox": [0, 0, 180, 85.05112877980659]})
    assert res.json()["outputs"]["result"]["count"] == pytest.approx(4.0, abs=0.01)


def test_a_terrain_tile_with_no_encoding_is_reported_with_its_code(client, terrain_png):
    body = run_node(client, {"operation": "point"}, {"raster": terrain_png, "tile": {"z": 1, "x": 1, "y": 0}, "lon": 10, "lat": 80}).json()
    assert body["result"] == "fail"
    assert body["error"].startswith("unstated_encoding:")


def test_an_encoding_given_for_a_raster_that_is_not_a_tile_is_refused(client, degrees_grid):
    body = run_node(client, {"operation": "metadata", "encoding": "terrarium"}, {"raster": degrees_grid}).json()
    assert body["result"] == "fail"
    assert body["error"].startswith("encoding_without_tile:")


@pytest.mark.parametrize("tile", [{"z": 1, "x": 5, "y": 0}, {"z": 1}, "1/1/0"])
def test_a_tile_that_does_not_exist_or_is_not_stated_fully_is_refused(client, terrain_png, tile):
    body = run_node(client, {"operation": "metadata", "encoding": "terrarium"}, {"raster": terrain_png, "tile": tile}).json()
    assert body["result"] == "fail"
    assert body["error"].startswith(("invalid_tile:", "missing_input:"))


def test_the_manifest_declares_the_tile_port(client):
    ports = {p["portName"]: p for p in client.get("/manifest").json()["ports"]}
    assert ports["tile"]["direction"] == "in" and ports["tile"]["required"] is False


@pytest.fixture
def raster_library_is_never_reached(monkeypatch):
    def reached(*args, **kwargs):
        raise AssertionError("the raster library was handed a source the service does not allow")

    monkeypatch.setattr("rasterio.open", reached)


@pytest.mark.parametrize("operation, inputs", [
    ("metadata", {}),
    ("point", {"lon": 1, "lat": 1}),
    ("zonal", {"bbox": [0, 0, 1, 1]}),
])
def test_a_raster_outside_the_allowed_folders_is_refused_before_it_is_opened(client, tmp_path, raster_library_is_never_reached, operation, inputs):
    outside = tmp_path.parent / "elsewhere.tif"
    body = run_node(client, {"operation": operation}, {"raster": str(outside), **inputs}).json()
    assert body["result"] == "fail"
    assert body["error"].startswith("source_not_allowed:")


@pytest.mark.parametrize("raster", ["https://data.example.org/dem.tif", "/vsicurl/https://data.example.org/dem.tif", "file:///etc/passwd"])
def test_a_web_address_is_refused_before_it_is_opened(client, raster_library_is_never_reached, raster):
    body = run_node(client, {"operation": "metadata"}, {"raster": raster}).json()
    assert body["result"] == "fail"
    assert body["error"].startswith("source_not_allowed:")


def test_a_terrain_tile_outside_the_allowed_folders_is_refused_before_it_is_opened(client, tmp_path, raster_library_is_never_reached):
    body = run_node(client, {"operation": "metadata", "encoding": "terrarium"}, {"raster": str(tmp_path.parent / "tile.png"), "tile": {"z": 1, "x": 1, "y": 0}}).json()
    assert body["result"] == "fail"
    assert body["error"].startswith("source_not_allowed:")


def test_a_service_with_no_sources_configured_reads_nothing(client, degrees_grid, monkeypatch):
    monkeypatch.setattr(appmod, "sources", Sources())
    body = run_node(client, {"operation": "metadata"}, {"raster": degrees_grid}).json()
    assert body["result"] == "fail"
    assert body["error"].startswith("source_not_allowed:")


def test_a_successful_answer_is_the_shape_phloem_reads_and_no_other(client, degrees_grid):
    body = run_node(client, {"operation": "metadata"}, {"raster": degrees_grid}).json()
    assert set(body) == {"result", "outputs", "error"}
    assert body["result"] == "success"
    assert body["error"] is None
    assert set(body["outputs"]) == {"result"}


def test_a_failed_answer_is_the_shape_phloem_reads_and_no_other(client, degrees_grid):
    body = run_node(client, {"operation": "point"}, {"raster": degrees_grid, "lon": 50, "lat": 50}).json()
    assert set(body) == {"result", "outputs", "error"}
    assert body["result"] == "fail"
    assert body["outputs"] == {}
    assert body["error"].startswith("outside_raster:")


def test_an_error_nobody_expected_is_a_fail_answer_not_a_server_error(client, degrees_grid, monkeypatch):
    def broken(raster):
        raise RuntimeError("the disk went away")

    monkeypatch.setitem(appmod.OPERATIONS, "metadata", lambda params, inputs: broken(inputs["raster"]))
    res = run_node(client, {"operation": "metadata"}, {"raster": degrees_grid})
    assert res.status_code == 200
    assert res.json()["result"] == "fail"
    assert res.json()["error"] == "the disk went away"


def test_a_raster_given_as_a_graph_reference_is_read_from_the_graph(client, degrees_grid):
    thing = "6d2b0c1e-0000-4000-8000-000000000009"
    with patch.object(host.mycelium, "thing_properties", new=AsyncMock(return_value={"Raster": {"Value": degrees_grid}})):
        body = run_node(client, {"operation": "metadata"}, {"raster": {"ref": {"thingId": thing, "property": "raster"}}}).json()
    assert body["result"] == "success"
    assert body["outputs"]["result"]["width"] == 10


def test_a_raster_given_as_a_graph_reference_is_still_held_to_the_allowed_sources(client, tmp_path, raster_library_is_never_reached):
    thing = "6d2b0c1e-0000-4000-8000-000000000009"
    outside = str(tmp_path.parent / "elsewhere.tif")
    with patch.object(host.mycelium, "thing_properties", new=AsyncMock(return_value={"Raster": {"Value": outside}})):
        body = run_node(client, {"operation": "metadata"}, {"raster": {"ref": {"thingId": thing, "property": "raster"}}}).json()
    assert body["result"] == "fail"
    assert body["error"].startswith("source_not_allowed:")
