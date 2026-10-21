"""Tests for the VillageOS Python managed-microservice example.

Run with:  pytest

The contract itself (the endpoints, inbound JWT validation, registration, arguments) is tested in
vos.Service.Python.Shared. This covers what this service adds: its handler, its demo endpoints, and
that it is wired to the library. Registration is mocked so the tests never touch the network.
"""

import asyncio
import base64
import json
import time
from unittest.mock import AsyncMock, patch

import httpx
import jwt
import pytest
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.hazmat.primitives.serialization import Encoding, PublicFormat
from fastapi.testclient import TestClient

import app as appmod
from app import app, demo_subscribe, host

THIS_HANDLER = "python-echo-handler"


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
    """Signs the way Mycelium does."""
    now = int(time.time())
    claims = {"iss": "VillageOS", "aud": THIS_HANDLER, "sub": "mycelium", "iat": now, "nbf": now, "exp": now + 60}
    return jwt.encode(claims, pair, algorithm="ES256")


def test_health_names_the_service(client):
    res = client.get("/health")
    assert res.status_code == 200
    assert res.json()["service"] == "Python"


def test_stats_carries_the_handler_id(client):
    body = client.get("/stats").json()
    assert body["service"] == "Python"
    assert "handlerId" in body


def test_handle_echoes_payload_when_unauthenticated(client):
    payload = {"relationshipId": "r1", "subjectName": "A", "properties": {"k": 1}}
    res = client.post("/handle", json=payload)
    assert res.status_code == 200
    body = res.json()
    assert body["success"] is True
    assert body["service"] == "Python"
    assert body["status"] == "handled"
    assert body["relationshipId"] == "r1"
    assert body["echo"] == payload
    assert "timestamp" in body


def test_handle_numbers_each_request(client):
    first = client.post("/handle", json={"relationshipId": "a"}).json()["requestNumber"]
    second = client.post("/handle", json={"relationshipId": "b"}).json()["requestNumber"]
    assert second == first + 1


def test_shutdown(client):
    res = client.post("/shutdown")
    assert res.status_code == 200
    assert "Shutting down" in res.json()["message"]


def test_handle_is_wired_to_the_librarys_token_check(client, signing):
    assert client.post("/handle", json={"relationshipId": "r2"}).status_code == 401
    res = client.post(
        "/handle", json={"relationshipId": "r3"}, headers={"Authorization": f"Bearer {make_token(signing)}"}
    )
    assert res.status_code == 200
    assert res.json()["relationshipId"] == "r3"


def test_the_demo_endpoints_are_behind_the_same_token_check(client, signing):
    assert client.post("/demo/write-kinds", json={"thingId": "t1"}).status_code == 401
    assert client.post("/demo/subscribe", json={}).status_code == 401


def test_write_kinds_demo_aggregates(client):
    with patch.object(host.mycelium, "set_fact", new=AsyncMock(return_value=7)), \
         patch.object(host.mycelium, "record_observation", new=AsyncMock()), \
         patch.object(host.mycelium, "record_observations", new=AsyncMock(return_value=2)), \
         patch.object(host.mycelium, "deposit_sediment", new=AsyncMock(return_value={"batchId": "b-9", "samples": 5})):
        res = client.post("/demo/write-kinds", json={"thingId": "t1"})
    assert res.status_code == 200
    body = res.json()
    assert body["factSequence"] == 7
    assert body["observationsAccepted"] == 3  # batch (2) + single (1)
    assert body["sedimentBatchId"] == "b-9"
    assert body["sedimentSamples"] == 5


def test_write_kinds_demo_deposits_readings_that_name_their_thing_as_the_sediment_route_reads_it(client):
    deposit = AsyncMock(return_value={"batchId": "b-9", "samples": 2})
    with patch.object(host.mycelium, "set_fact", new=AsyncMock(return_value=7)),          patch.object(host.mycelium, "record_observation", new=AsyncMock()),          patch.object(host.mycelium, "record_observations", new=AsyncMock(return_value=2)),          patch.object(host.mycelium, "deposit_sediment", new=deposit):
        client.post("/demo/write-kinds", json={"thingId": "t1"})
    readings = deposit.await_args.args[0]
    assert [reading["objectId"] for reading in readings] == ["t1", "t1"]
    assert all("thingId" not in reading for reading in readings)


def test_the_write_demo_answers_a_refused_write_with_what_was_refused(client):
    with patch.object(host.mycelium, "set_fact", new=AsyncMock(side_effect=RuntimeError("fact write returned 405"))):
        res = client.post("/demo/write-kinds", json={"thingId": "t1"})

    assert res.status_code == 500
    assert res.json() == {"error": "fact write returned 405"}


def test_the_subscribe_demo_answers_a_refused_subscription_with_what_was_refused(client):
    with patch.object(host.mycelium, "subscribe", new=AsyncMock(side_effect=RuntimeError("subscribe returned 403"))):
        res = client.post("/demo/subscribe", json={})

    assert res.status_code == 500
    assert res.json() == {"error": "subscribe returned 403"}


def test_the_demo_routes_answer_a_refused_connection_with_why(client):
    refused = httpx.ConnectError("certificate verify failed: self-signed certificate")
    with patch.object(host.mycelium, "set_fact", new=AsyncMock(side_effect=refused)),          patch.object(host.mycelium, "subscribe", new=AsyncMock(side_effect=refused)):
        writes = client.post("/demo/write-kinds", json={"thingId": "t1"})
        subscription = client.post("/demo/subscribe", json={})

    for answer in (writes, subscription):
        assert answer.status_code == 500
        assert "certificate" in answer.json()["error"].lower()


def test_write_kinds_demo_requires_thing_id(client):
    res = client.post("/demo/write-kinds", json={})
    assert res.status_code == 400


def test_subscribe_demo_passes_the_requested_slice_on(client):
    with patch("app.demo_subscribe", new=AsyncMock(return_value={"things": 2})) as demo:
        res = client.post("/demo/subscribe", json={"type": "Pump", "predicate": "feeds"})
    assert res.json() == {"things": 2}
    demo.assert_awaited_once_with("Pump", "feeds")


def test_subscribe_demo_defaults_to_batteries_that_power(client):
    with patch("app.demo_subscribe", new=AsyncMock(return_value={})) as demo:
        client.post("/demo/subscribe")
    demo.assert_awaited_once_with("Battery", "powers")


def subscription_handler(captured):
    def handle(request: httpx.Request) -> httpx.Response:
        captured.append(request)
        if request.url.path == "/api/subscriptions" and request.method == "POST":
            return httpx.Response(
                200,
                json={
                    "subscriptionId": "s-1",
                    "watermark": 42,
                    # Things and relationships as the model routes write them, capitals included.
                    "snapshot": {
                        "things": [{"Id": "t1", "Name": "Battery-1"}, {"Id": "t2", "Name": "Inverter-7"}],
                        "relationships": [{"Id": "r1"}],
                    },
                },
            )
        return httpx.Response(200)  # DELETE unsubscribe

    return handle


def test_demo_subscribe_summarises_and_unsubscribes(monkeypatch):
    monkeypatch.setattr(appmod.config, "mycelium_url", "http://mycelium.test")
    monkeypatch.setattr(appmod.config, "token", "tok")
    captured = []

    async def run():
        async with httpx.AsyncClient(transport=httpx.MockTransport(subscription_handler(captured))) as c:
            return await demo_subscribe("Battery", "powers", client=c)

    result = asyncio.run(run())
    assert result["things"] == 2
    assert result["relationships"] == 1
    assert result["thingNames"] == ["Battery-1", "Inverter-7"]
    assert captured[0].method == "POST"
    assert json.loads(captured[0].content)["types"] == ["Battery"]
    assert captured[1].method == "DELETE"
    assert captured[1].url.path == "/api/subscriptions/s-1"
