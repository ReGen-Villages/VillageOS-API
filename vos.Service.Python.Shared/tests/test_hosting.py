import asyncio
import os
import time
from unittest.mock import AsyncMock, patch

import httpx
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


def until(condition, expectation: str, seconds: float = 5.0):
    deadline = time.monotonic() + seconds
    while not condition():
        assert time.monotonic() < deadline, expectation
        time.sleep(0.01)


def broker_answering(handle):
    return patch("vos_service.mycelium.platform_client", lambda timeout: httpx.AsyncClient(transport=httpx.MockTransport(handle)))


def test_startup_registers_under_the_services_name_and_id(host):
    register = AsyncMock(return_value=True)
    with patch.object(host.mycelium, "register", new=register):
        with TestClient(host.app):
            until(lambda: register.await_count == 1, "startup registers the service")
    register.assert_awaited_once_with("Example", host.handler_id, client=None)


def test_bug8746_a_registration_the_starting_broker_refuses_is_asked_again_until_it_is_taken(host):
    asks = []

    def handle(request):
        asks.append(request)
        return httpx.Response(200) if len(asks) == 3 else httpx.Response(503, headers={"Retry-After": "0"})

    with broker_answering(handle), TestClient(host.app) as c:
        until(lambda: len(asks) == 3, "the service asks again until the broker takes it")
        assert c.get("/health").status_code == 200


def test_shutdown_stops_the_asking(host):
    asks = []

    def handle(request):
        asks.append(request)
        return httpx.Response(503, headers={"Retry-After": "0"})

    async def asks_after_shutdown():
        async with host.app.router.lifespan_context(host.app):
            while not asks:
                await asyncio.sleep(0.01)
        at_shutdown = len(asks)
        await asyncio.sleep(0.05)
        return len(asks) - at_shutdown

    with broker_answering(handle):
        assert asyncio.run(asyncio.wait_for(asks_after_shutdown(), 5)) == 0


def test_a_shutdown_cancelled_from_outside_while_the_service_still_asks_is_cancelled(host):
    async def cancel_the_shutdown():
        cancel_seen, release = asyncio.Event(), asyncio.Event()

        async def register(*args, **kwargs):
            try:
                await asyncio.Event().wait()
            except asyncio.CancelledError:
                cancel_seen.set()
                await release.wait()
                raise

        with patch.object(host.mycelium, "register", new=register):
            lifespan = host.app.router.lifespan_context(host.app)
            await lifespan.__aenter__()
            shutdown = asyncio.create_task(lifespan.__aexit__(None, None, None))
            await cancel_seen.wait()
            shutdown.cancel()
            release.set()
            with pytest.raises(asyncio.CancelledError):
                await shutdown
        await asyncio.sleep(0)
        return [task for task in asyncio.all_tasks() if task is not asyncio.current_task() and not task.done()]

    assert asyncio.run(asyncio.wait_for(cancel_the_shutdown(), 5)) == []


def test_a_failed_registration_does_not_stop_the_service(host, capsys):
    register = AsyncMock(side_effect=RuntimeError("no broker"))
    with patch.object(host.mycelium, "register", new=register):
        with TestClient(host.app) as c:
            until(lambda: register.await_count == 1, "startup tries to register")
            assert c.get("/health").status_code == 200
    assert "registration failed: no broker" in capsys.readouterr().err


# The broker's liveness monitor removes a registration after repeated failed health checks, unless the broker supervises the service.
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


def test_with_neither_a_key_nor_a_token_no_call_is_made_and_the_service_says_so(host, config, capsys):
    config.token = None
    config.api_key = None
    with patch("httpx.AsyncClient.request", new=AsyncMock()) as request:
        with TestClient(host.app):
            pass

    assert request.await_count == 0, "a call carrying no credential is always refused"
    said = capsys.readouterr().out
    assert said.count("\n") == 1 and "ApiKey" in said and "Token" in said


# A service the platform did not start is stopped through this route alone.
def test_shutdown_stops_the_server_once_it_has_answered(client, host):
    class Serving:
        should_exit = False

    host.server = Serving()

    assert client.post("/shutdown").status_code == 200
    assert host.server.should_exit is True
