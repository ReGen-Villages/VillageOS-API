import asyncio
import json
from datetime import datetime, timedelta, timezone

import httpx
import jwt
import pytest

from vos_service import Config, MyceliumClient, sediment_reading


@pytest.fixture
def config():
    return Config(port=5103, mycelium_url="http://mycelium.test", token="tok")


@pytest.fixture
def mycelium(config):
    return MyceliumClient(config)


def run_against(handler, coro_factory):
    """Run `coro_factory(client)` against a fake Mycelium answering through `handler`."""
    async def run():
        async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as c:
            return await coro_factory(c)

    return asyncio.run(run())


def write_handler(captured):
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


def subscription_handler(captured):
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
        return httpx.Response(200)

    return handle


def test_token_is_the_one_supplied_in_the_environment(mycelium):
    assert asyncio.run(mycelium.token()) == "tok"


A_MOMENT = datetime(2026, 10, 2, 12, 0, tzinfo=timezone.utc)
NOT_A_SIGNING_KEY = "k" * 32


def token_expiring(at: datetime | None) -> str:
    claims = {"sub": "a-service"} if at is None else {"sub": "a-service", "exp": int(at.timestamp())}
    return jwt.encode(claims, NOT_A_SIGNING_KEY, algorithm="HS256")


@pytest.fixture
def holding_a_key():
    """A service holding an API key and no token, with a clock the test moves."""
    config = Config(port=5103, mycelium_url="http://mycelium.test", api_key="vos_ak_example")
    clock = {"now": A_MOMENT}
    return config, MyceliumClient(config, now=lambda: clock["now"]), clock


def exchanges_answering(captured, minted):
    def handle(request: httpx.Request) -> httpx.Response:
        captured.append(request)
        return httpx.Response(200, json={"token": minted()})

    return handle


def tokens_asked_for(mycelium, handler, times, clock=None, between=timedelta(0)):
    async def run():
        async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as c:
            answered = []
            for _ in range(times):
                answered.append(await mycelium.token(client=c))
                if clock is not None:
                    clock["now"] += between
            return answered

    return asyncio.run(run())


def test_a_key_is_exchanged_in_the_key_header_and_the_token_is_held(holding_a_key):
    _, mycelium, _ = holding_a_key
    captured = []
    minted = token_expiring(A_MOMENT + timedelta(minutes=5))

    answered = tokens_asked_for(mycelium, exchanges_answering(captured, lambda: minted), times=3)

    assert answered == [minted] * 3
    assert len(captured) == 1, "the token is held, so the key is exchanged once and not on every call"
    assert captured[0].url.path == "/api/auth/token"
    assert captured[0].headers["X-API-Key"] == "vos_ak_example"


def test_a_held_token_is_exchanged_again_shortly_before_it_runs_out(holding_a_key):
    _, mycelium, clock = holding_a_key
    captured = []
    lifetime = timedelta(minutes=5)

    tokens_asked_for(
        mycelium, exchanges_answering(captured, lambda: token_expiring(clock["now"] + lifetime)),
        times=3, clock=clock, between=lifetime - timedelta(seconds=20))

    assert len(captured) == 3, "twenty seconds from running out is inside the half minute a token is replaced in"


def test_a_token_that_states_no_expiry_is_not_held(holding_a_key):
    _, mycelium, _ = holding_a_key
    with pytest.raises(RuntimeError, match="no expiry"):
        tokens_asked_for(mycelium, exchanges_answering([], lambda: token_expiring(None)), times=1)


def test_a_refused_key_says_what_the_platform_answered(holding_a_key):
    _, mycelium, _ = holding_a_key
    with pytest.raises(RuntimeError, match="401"):
        tokens_asked_for(mycelium, lambda request: httpx.Response(401), times=1)


def test_with_neither_a_key_nor_a_token_there_is_nothing_to_present(holding_a_key):
    config, mycelium, _ = holding_a_key
    config.api_key = None
    with pytest.raises(RuntimeError, match="neither ApiKey nor Token is set"):
        tokens_asked_for(mycelium, lambda request: httpx.Response(500), times=1)


def test_a_key_is_presented_before_a_token_given_beside_it(holding_a_key):
    config, mycelium, _ = holding_a_key
    config.token = "a-launch-token"
    minted = token_expiring(A_MOMENT + timedelta(minutes=5))

    assert tokens_asked_for(mycelium, exchanges_answering([], lambda: minted), times=1) == [minted]


def test_a_write_carries_the_token_the_key_was_exchanged_for(holding_a_key):
    _, mycelium, _ = holding_a_key
    minted = token_expiring(A_MOMENT + timedelta(minutes=5))
    seen = []

    def handle(request):
        seen.append(request)
        if request.url.path == "/api/auth/token":
            return httpx.Response(200, json={"token": minted})
        return httpx.Response(201, json={"sequenceNumber": 1})

    run_against(handle, lambda c: mycelium.set_fact("t1", "status", "active", client=c))
    assert seen[-1].headers["Authorization"] == f"Bearer {minted}"


def test_register_posts_the_envelope_with_the_bearer_token(mycelium):
    captured = []

    def handle(request):
        captured.append(request)
        return httpx.Response(201)

    assert run_against(handle, lambda c: mycelium.register("Python", "h-1", client=c)) is True
    request = captured[0]
    assert request.url.path == "/api/mycelium/register"
    assert request.headers["Authorization"] == "Bearer tok"
    assert json.loads(request.content) == {
        "handlerId": "h-1",
        "serviceName": "Python",
        "endpointUrl": "http://localhost:5103",
        "startCommand": "endpoint-service",
        "stopEndpoint": "http://localhost:5103/shutdown",
        "healthEndpoint": "http://localhost:5103/health",
    }


def test_register_reports_a_refusal(mycelium):
    assert run_against(lambda r: httpx.Response(403), lambda c: mycelium.register("Python", "h-1", client=c)) is False


def test_set_fact_posts_value_and_returns_sequence(mycelium):
    captured = []
    seq = run_against(write_handler(captured), lambda c: mycelium.set_fact("t1", "status", "active", client=c))
    assert seq == 42
    request = captured[0]
    assert request.url.path == "/api/things/t1/properties/status/facts"
    assert request.headers["Authorization"] == "Bearer tok"
    assert json.loads(request.content)["value"] == "active"


def test_set_fact_405_raises(mycelium):
    with pytest.raises(RuntimeError, match="405"):
        run_against(lambda r: httpx.Response(405), lambda c: mycelium.set_fact("t1", "temperature", 1, client=c))


def test_a_property_name_is_escaped_in_the_address(mycelium):
    captured = []
    run_against(write_handler(captured), lambda c: mycelium.set_fact("t1", "a/b c", 1, client=c))
    assert captured[0].url.raw_path.decode().startswith("/api/things/t1/properties/a%2Fb%20c/")


def test_record_observation_posts_value_and_observed_at(mycelium):
    captured = []
    run_against(
        write_handler(captured),
        lambda c: mycelium.record_observation("t1", "temperature", 21.5, "2026-06-20T14:00:00Z", client=c),
    )
    request = captured[0]
    assert request.url.path == "/api/things/t1/properties/temperature/observations"
    body = json.loads(request.content)
    assert body["value"] == 21.5
    assert body["observedAt"] == "2026-06-20T14:00:00Z"


def test_record_observation_omits_observed_at(mycelium):
    captured = []
    run_against(write_handler(captured), lambda c: mycelium.record_observation("t1", "temperature", 21.5, client=c))
    assert "observedAt" not in json.loads(captured[0].content)


def test_record_observations_batch_returns_accepted(mycelium):
    captured = []
    n = run_against(
        write_handler(captured),
        lambda c: mycelium.record_observations(
            "t1", [{"property": "temperature", "value": 21.7}, {"property": "flow", "value": 3.1}], client=c),
    )
    assert n == 2
    assert captured[0].url.path == "/api/things/t1/observations"
    assert isinstance(json.loads(captured[0].content), list)


def test_record_observations_empty_makes_no_call(mycelium):
    captured = []
    n = run_against(write_handler(captured), lambda c: mycelium.record_observations("t1", [], client=c))
    assert n == 0
    assert captured == []


def test_deposit_sediment_posts_readings_and_returns_summary(mycelium):
    captured = []
    readings = [sediment_reading("t1", "flow", 1.0, "2026-06-19T00:00:00Z")]
    result = run_against(write_handler(captured), lambda c: mycelium.deposit_sediment(readings, client=c))
    assert result["batchId"] == "b-1"
    assert result["samples"] == 10
    assert captured[0].url.path == "/api/sediment"
    assert json.loads(captured[0].content)[0] == readings[0]


def test_a_sediment_reading_names_its_thing_the_way_the_sediment_route_reads_it():
    assert sediment_reading("t1", "flow", 1.0, "2026-06-19T00:00:00Z") == {
        "objectId": "t1", "property": "flow", "value": 1.0, "observedAt": "2026-06-19T00:00:00Z"}


def test_deposit_sediment_empty_raises(mycelium):
    with pytest.raises(ValueError, match="at least one reading"):
        asyncio.run(mycelium.deposit_sediment([]))


def test_slice_by_type_and_traverse_builds_selector():
    selector = MyceliumClient.slice_by_type_and_traverse("Battery", "powers")
    assert selector["types"] == ["Battery"]
    assert selector["traverse"][0]["predicate"] == "powers"
    assert selector["traverse"][0]["direction"] == "outgoing"


def test_subscribe_posts_selector_and_returns_closure(mycelium):
    captured = []
    selector = MyceliumClient.slice_by_type_and_traverse("Battery", "powers")
    sub = run_against(subscription_handler(captured), lambda c: mycelium.subscribe(selector, client=c))
    assert sub["subscriptionId"] == "s-1"
    assert len(sub["snapshot"]["things"]) == 2
    request = captured[0]
    assert request.url.path == "/api/subscriptions"
    assert request.headers["Authorization"] == "Bearer tok"
    assert json.loads(request.content)["types"] == ["Battery"]


def test_unsubscribe_releases_the_subscription(mycelium):
    captured = []
    run_against(subscription_handler(captured), lambda c: mycelium.unsubscribe("s-1", client=c))
    assert captured[0].method == "DELETE"
    assert captured[0].url.path == "/api/subscriptions/s-1"
    assert captured[0].headers["Authorization"] == "Bearer tok"
