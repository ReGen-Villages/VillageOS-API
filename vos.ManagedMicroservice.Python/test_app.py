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
