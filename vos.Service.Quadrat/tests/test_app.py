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

THIS_HANDLER = "quadrat-handler"
RUN = {"runId": "6d2b0c1e-0000-4000-8000-000000000001", "nodeId": "6d2b0c1e-0000-4000-8000-000000000002"}


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


def run_node(client, params, inputs):
    return client.post("/handle", json={**RUN, "params": params, "inputs": inputs})


def test_health_names_the_service(client):
    res = client.get("/health")
    assert res.status_code == 200
    assert res.json()["service"] == "Quadrat"


def test_metadata_answers_the_rasters_shape(client, degrees_grid):
    res = run_node(client, {"operation": "metadata"}, {"raster": degrees_grid})
    body = res.json()
    assert res.status_code == 200
    assert body["success"] is True
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
    assert body["success"] is False
    assert "bbox" in body["error"] and "geometry" in body["error"]


def test_a_request_quadrat_will_not_answer_is_reported_with_its_code(client, degrees_grid):
    res = run_node(client, {"operation": "point"}, {"raster": degrees_grid, "lon": 50, "lat": 50})
    body = res.json()
    assert res.status_code == 200
    assert body["success"] is False
    assert body["outputs"] == {}
    assert body["error"].startswith("outside_raster:")


def test_an_unreadable_raster_is_reported_not_raised(client, tmp_path):
    res = run_node(client, {"operation": "metadata"}, {"raster": str(tmp_path / "missing.tif")})
    body = res.json()
    assert res.status_code == 200
    assert body["success"] is False
    assert body["error"].startswith("unreadable_raster:")


@pytest.mark.parametrize("params", [{}, {"operation": "histogram"}])
def test_a_missing_or_unknown_operation_names_the_ones_there_are(client, degrees_grid, params):
    body = run_node(client, params, {"raster": degrees_grid}).json()
    assert body["success"] is False
    assert "metadata" in body["error"] and "point" in body["error"] and "zonal" in body["error"]


@pytest.mark.parametrize("inputs, missing", [
    ({}, "raster"),
    ({"raster": "r.tif", "lat": 1.0}, "lon"),
    ({"raster": "r.tif", "lon": 1.0}, "lat"),
    ({"raster": "r.tif"}, "bbox"),
])
def test_an_operation_missing_an_input_says_which(client, inputs, missing):
    operation = "zonal" if missing == "bbox" else "point" if missing in ("lon", "lat") else "metadata"
    body = run_node(client, {"operation": operation}, inputs).json()
    assert body["success"] is False
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
    assert ok.json()["success"] is True
