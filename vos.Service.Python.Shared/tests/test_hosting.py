import os
from unittest.mock import AsyncMock, patch

import pytest
from fastapi import Depends
from fastapi.testclient import TestClient

from vos_service import Config, ServiceHost


async def echo(payload, request_number):
    return {"handled": True, "requestNumber": request_number, "echo": payload}


@pytest.fixture
def config():
    return Config(port=5103, mycelium_url="https://mycelium.test", token="tok")


@pytest.fixture
def host(config):
    return ServiceHost("Example", config, echo)


@pytest.fixture
def client(host):
    with patch.object(host.mycelium, "register", new=AsyncMock(return_value=True)):
        with TestClient(host.app) as c:
            yield c


def test_health(client):
    res = client.get("/health")
    assert res.status_code == 200
    body = res.json()
    assert body["status"] == "Healthy"
    assert body["service"] == "Example"
    assert body["processId"] == os.getpid()
    assert body["requestsProcessed"] == 0


def test_stats(client, host):
    body = client.get("/stats").json()
    assert body["service"] == "Example"
    assert body["version"] == "1.0.0"
    assert body["handlerId"] == host.handler_id
    assert body["myceliumUrl"] == "https://mycelium.test"


def test_a_service_can_state_its_own_version(config):
    host = ServiceHost("Example", config, echo, version="2.3.4")
    with patch.object(host.mycelium, "register", new=AsyncMock(return_value=True)):
        with TestClient(host.app) as c:
            assert c.get("/stats").json()["version"] == "2.3.4"


def test_handle_passes_the_payload_to_the_handler_and_returns_its_answer(client):
    payload = {"relationshipId": "r1", "subjectName": "A", "properties": {"k": 1}}
    res = client.post("/handle", json=payload)
    assert res.status_code == 200
    assert res.json() == {"handled": True, "requestNumber": 1, "echo": payload}


def test_each_call_to_handle_is_numbered_and_counted(client):
    client.post("/handle", json={})
    second = client.post("/handle", json={})
    assert second.json()["requestNumber"] == 2
    assert client.get("/health").json()["requestsProcessed"] == 2


def test_shutdown_acknowledges_by_name(client):
    res = client.post("/shutdown")
    assert res.status_code == 200
    assert res.json()["message"] == "Shutting down Example microservice"


def test_handle_and_shutdown_need_a_token_once_a_verification_key_is_set(client, config):
    config.verification_key = "ZW52aXJvbm1lbnQta2V5"
    config.issuer = "VillageOS"
    config.audience = "example-handler"
    assert client.post("/handle", json={}).status_code == 401
    assert client.post("/shutdown").status_code == 401
    assert client.get("/health").status_code == 200
    assert client.get("/stats").status_code == 200


def test_a_service_can_add_its_own_protected_route(host, config):
    @host.app.post("/own")
    def own(_: None = Depends(host.verify_request)) -> dict:
        return {"own": True}

    with patch.object(host.mycelium, "register", new=AsyncMock(return_value=True)):
        with TestClient(host.app) as c:
            assert c.post("/own").json() == {"own": True}
            config.verification_key = "ZW52aXJvbm1lbnQta2V5"
            assert c.post("/own").status_code == 401


def test_startup_registers_under_the_services_name_and_id(host):
    register = AsyncMock(return_value=True)
    with patch.object(host.mycelium, "register", new=register):
        with TestClient(host.app):
            pass
    register.assert_awaited_once_with("Example", host.handler_id)


def test_a_failed_registration_does_not_stop_the_service(host, capsys):
    with patch.object(host.mycelium, "register", new=AsyncMock(side_effect=RuntimeError("no broker"))):
        with TestClient(host.app) as c:
            assert c.get("/health").status_code == 200
    assert "registration failed: no broker" in capsys.readouterr().err


# The deregistration route is admin-only; the broker's liveness monitor removes a registration whose service stops answering.
def test_shutdown_does_not_ask_the_broker_to_withdraw(host):
    with patch.object(host.mycelium, "register", new=AsyncMock(return_value=True)), \
         patch("httpx.AsyncClient.request", new=AsyncMock()) as request:
        with TestClient(host.app):
            pass
    assert request.await_count == 0, "a service cannot deregister itself, so shutdown must not try"


def test_two_hosts_do_not_share_a_handler_id(config):
    first, second = ServiceHost("A", config, echo), ServiceHost("B", config, echo)
    assert first.handler_id != second.handler_id


def test_run_without_a_port_prints_the_usage_and_exits(capsys):
    host = ServiceHost("Example", Config(), echo)
    with pytest.raises(SystemExit) as exited:
        host.run("service.py")
    assert exited.value.code == 1
    assert ".venv/bin/python service.py" in capsys.readouterr().err
