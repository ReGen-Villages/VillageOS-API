"""Tests for the VillageOS Python managed-microservice example.

Run with:  pytest

Covers the four contract endpoints and the inbound HS256 JWT validation.
Registration/deregistration are mocked so the tests never touch the network.
"""

import base64
import time
from unittest.mock import AsyncMock, patch

import jwt
import pytest
from fastapi.testclient import TestClient

import app as appmod
from app import app


@pytest.fixture
def client():
    with patch("app.register_with_mycelium", new=AsyncMock(return_value=True)):
        with patch("app.deregister_from_mycelium", new=AsyncMock()):
            with TestClient(app) as c:
                yield c


@pytest.fixture
def signing(monkeypatch):
    """Enable JWT auth with a known key; yields (base64_key, raw_key_bytes)."""
    raw = b"vos-test-signing-key-0123456789ab"
    b64 = base64.b64encode(raw).decode()
    monkeypatch.setattr(appmod.config, "signing_key", b64)
    monkeypatch.setattr(appmod.config, "issuer", "VillageOS")
    monkeypatch.setattr(appmod.config, "audience", "VosClients")
    return b64, raw


def make_token(raw_key: bytes, **overrides) -> str:
    now = int(time.time())
    claims = {
        "iss": "VillageOS",
        "aud": "VosClients",
        "sub": "mycelium",
        "vos:token_type": "mycelium_request",
        "iat": now,
        "nbf": now,
        "exp": now + 60,
    }
    claims.update(overrides)
    return jwt.encode(claims, raw_key, algorithm="HS256")


def test_health(client):
    res = client.get("/health")
    assert res.status_code == 200
    assert res.json()["status"] == "Healthy"
    assert res.json()["service"] == "Python"


def test_stats(client):
    res = client.get("/stats")
    body = res.json()
    assert res.status_code == 200
    assert body["service"] == "Python"
    assert "handlerId" in body


def test_handle_echoes_payload_when_unauthenticated(client):
    payload = {"relationshipId": "r1", "subjectName": "A", "properties": {"k": 1}}
    res = client.post("/handle", json=payload)
    assert res.status_code == 200
    body = res.json()
    assert body["success"] is True
    assert body["relationshipId"] == "r1"
    assert body["echo"] == payload


def test_shutdown(client):
    res = client.post("/shutdown")
    assert res.status_code == 200
    assert "Shutting down" in res.json()["message"]


def test_handle_rejects_missing_token_when_auth_enabled(client, signing):
    res = client.post("/handle", json={"relationshipId": "r2"})
    assert res.status_code == 401


def test_handle_accepts_valid_token(client, signing):
    _, raw = signing
    token = make_token(raw)
    res = client.post(
        "/handle", json={"relationshipId": "r3"}, headers={"Authorization": f"Bearer {token}"}
    )
    assert res.status_code == 200
    assert res.json()["relationshipId"] == "r3"


def test_handle_rejects_tampered_token(client, signing):
    _, raw = signing
    token = make_token(raw) + "x"
    res = client.post(
        "/handle", json={"relationshipId": "r4"}, headers={"Authorization": f"Bearer {token}"}
    )
    assert res.status_code == 401


def test_handle_rejects_expired_token(client, signing):
    _, raw = signing
    token = make_token(raw, exp=int(time.time()) - 120)
    res = client.post(
        "/handle", json={"relationshipId": "r5"}, headers={"Authorization": f"Bearer {token}"}
    )
    assert res.status_code == 401


def test_handle_rejects_wrong_issuer(client, signing):
    _, raw = signing
    token = make_token(raw, iss="Attacker")
    res = client.post(
        "/handle", json={"relationshipId": "r6"}, headers={"Authorization": f"Bearer {token}"}
    )
    assert res.status_code == 401


# ---- Write kinds (Fact / Observation / Sediment) ----
import asyncio
import json

import httpx

from app import set_fact, record_observation, record_observations, deposit_sediment


@pytest.fixture
def mycelium(monkeypatch):
    """Point the module at a fake Mycelium with a pre-supplied token (no /api/auth/token call)."""
    monkeypatch.setattr(appmod.config, "mycelium_url", "http://mycelium.test")
    monkeypatch.setattr(appmod.config, "token", "tok")


def _handler(captured):
    def handle(request: httpx.Request) -> httpx.Response:
        captured.append(request)
        path = request.url.path
        if path.endswith("/facts"):
            return httpx.Response(201, json={"sequenceNumber": 42, "value": "active"})
        if "/properties/" in path and path.endswith("/observations"):
            return httpx.Response(202)
        if path.endswith("/observations"):
            return httpx.Response(202, json={"accepted": 2})
        if path == "/api/sediment":
            return httpx.Response(202, json={"batchId": "b-1", "series": 1, "buckets": 3, "samples": 10})
        return httpx.Response(404)

    return handle


def _run(captured, coro_factory):
    async def run():
        async with httpx.AsyncClient(transport=httpx.MockTransport(_handler(captured))) as c:
            return await coro_factory(c)

    return asyncio.run(run())


def test_set_fact_posts_value_and_returns_sequence(mycelium):
    captured = []
    seq = _run(captured, lambda c: set_fact("t1", "status", "active", client=c))
    assert seq == 42
    req = captured[0]
    assert req.url.path == "/api/things/t1/properties/status/facts"
    assert req.headers["Authorization"] == "Bearer tok"
    assert json.loads(req.content)["value"] == "active"


def test_set_fact_405_raises(mycelium):
    async def run():
        async with httpx.AsyncClient(transport=httpx.MockTransport(lambda r: httpx.Response(405))) as c:
            await set_fact("t1", "temperature", 1, client=c)

    with pytest.raises(RuntimeError, match="405"):
        asyncio.run(run())


def test_record_observation_posts_value_and_observed_at(mycelium):
    captured = []
    _run(captured, lambda c: record_observation("t1", "temperature", 21.5, "2026-06-20T14:00:00Z", client=c))
    req = captured[0]
    assert req.url.path == "/api/things/t1/properties/temperature/observations"
    body = json.loads(req.content)
    assert body["value"] == 21.5
    assert body["observedAt"] == "2026-06-20T14:00:00Z"


def test_record_observation_omits_observed_at(mycelium):
    captured = []
    _run(captured, lambda c: record_observation("t1", "temperature", 21.5, client=c))
    assert "observedAt" not in json.loads(captured[0].content)


def test_record_observations_batch_returns_accepted(mycelium):
    captured = []
    n = _run(
        captured,
        lambda c: record_observations("t1", [{"property": "temperature", "value": 21.7}, {"property": "flow", "value": 3.1}], client=c),
    )
    assert n == 2
    assert captured[0].url.path == "/api/things/t1/observations"
    assert isinstance(json.loads(captured[0].content), list)


def test_record_observations_empty_makes_no_call(mycelium):
    captured = []
    n = _run(captured, lambda c: record_observations("t1", [], client=c))
    assert n == 0
    assert captured == []


def test_deposit_sediment_posts_readings_and_returns_summary(mycelium):
    captured = []
    res = _run(captured, lambda c: deposit_sediment([{"thingId": "t1", "property": "flow", "value": 1.0, "observedAt": "2026-06-19T00:00:00Z"}], client=c))
    assert res["batchId"] == "b-1"
    assert res["samples"] == 10
    assert captured[0].url.path == "/api/sediment"
    assert "observedAt" in json.loads(captured[0].content)[0]


def test_deposit_sediment_empty_raises():
    with pytest.raises(ValueError, match="at least one reading"):
        asyncio.run(deposit_sediment([]))


def test_demo_endpoint_aggregates(client):
    with patch("app.set_fact", new=AsyncMock(return_value=7)), \
         patch("app.record_observation", new=AsyncMock()), \
         patch("app.record_observations", new=AsyncMock(return_value=2)), \
         patch("app.deposit_sediment", new=AsyncMock(return_value={"batchId": "b-9", "samples": 5})):
        res = client.post("/demo/write-kinds", json={"thingId": "t1"})
    assert res.status_code == 200
    body = res.json()
    assert body["factSequence"] == 7
    assert body["observationsAccepted"] == 3  # batch (2) + single (1)
    assert body["sedimentBatchId"] == "b-9"
    assert body["sedimentSamples"] == 5


def test_demo_endpoint_requires_thing_id(client):
    res = client.post("/demo/write-kinds", json={})
    assert res.status_code == 400
