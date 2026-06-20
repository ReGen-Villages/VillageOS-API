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


# ---- Snapshot selector ----
import asyncio
import json

import httpx

from app import subscribe, unsubscribe, slice_by_type_and_traverse, demo_subscribe


@pytest.fixture
def mycelium(monkeypatch):
    monkeypatch.setattr(appmod.config, "mycelium_url", "http://mycelium.test")
    monkeypatch.setattr(appmod.config, "token", "tok")


def _sel_handler(captured):
    def handle(request: httpx.Request) -> httpx.Response:
        captured.append(request)
        if request.url.path == "/api/subscriptions" and request.method == "POST":
            return httpx.Response(
                200,
                json={
                    "subscriptionId": "s-1",
                    "watermark": 42,
                    "snapshot": {
                        "things": [{"id": "t1", "name": "Battery-1"}, {"id": "t2", "name": "Inverter-7"}],
                        "relationships": [{"id": "r1"}],
                    },
                },
            )
        return httpx.Response(200)  # DELETE unsubscribe

    return handle


def _run_sel(captured, coro_factory):
    async def run():
        async with httpx.AsyncClient(transport=httpx.MockTransport(_sel_handler(captured))) as c:
            return await coro_factory(c)

    return asyncio.run(run())


def test_slice_by_type_and_traverse_builds_selector():
    sel = slice_by_type_and_traverse("Battery", "powers")
    assert sel["types"] == ["Battery"]
    assert sel["traverse"][0]["predicate"] == "powers"
    assert sel["traverse"][0]["direction"] == "outgoing"


def test_subscribe_posts_selector_and_returns_closure(mycelium):
    captured = []
    sub = _run_sel(captured, lambda c: subscribe(slice_by_type_and_traverse("Battery", "powers"), client=c))
    assert sub["subscriptionId"] == "s-1"
    assert sub["watermark"] == 42
    assert len(sub["snapshot"]["things"]) == 2
    req = captured[0]
    assert req.url.path == "/api/subscriptions"
    assert req.headers["Authorization"] == "Bearer tok"
    body = json.loads(req.content)
    assert body["types"] == ["Battery"]
    assert body["traverse"][0]["predicate"] == "powers"


def test_demo_subscribe_summarises_and_unsubscribes(mycelium):
    captured = []
    result = _run_sel(captured, lambda c: demo_subscribe("Battery", "powers", client=c))
    assert result["things"] == 2
    assert result["relationships"] == 1
    assert result["thingNames"] == ["Battery-1", "Inverter-7"]
    assert captured[0].method == "POST"
    assert captured[1].method == "DELETE"
    assert captured[1].url.path == "/api/subscriptions/s-1"
