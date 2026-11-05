import asyncio
import base64
import threading
import time
import uuid
from unittest.mock import AsyncMock, patch

import jwt
import pytest
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.hazmat.primitives.serialization import Encoding, PublicFormat
from fastapi.testclient import TestClient

from vos_service import Config, NodeService, Port, ServiceHost, Setting

RUN_ID = "6d2b0c1e-0000-4000-8000-000000000001"
NODE_ID = "6d2b0c1e-0000-4000-8000-000000000002"
THING = "6d2b0c1e-0000-4000-8000-000000000003"
ENVELOPE = {"runId": RUN_ID, "nodeId": NODE_ID, "params": {}, "inputs": {}}
PORTS = [Port.input("message", "string", required=True), Port.output("echo", "string")]


def phloem_reads(body):
    """The answer as Phloem's PipelineExecutor.ParseNodeResponse reads it: `result` must be a string saying
    success or fail; outputs are read from an object; a fail carries its error."""
    result = body.get("result") if isinstance(body, dict) else None
    assert result in ("success", "fail"), f"Phloem fails an answer whose result is {result!r}"
    outputs = body.get("outputs") if isinstance(body.get("outputs"), dict) else {}
    error = body.get("error") if isinstance(body.get("error"), str) else None
    if result == "fail":
        assert error, "a fail answer says why"
    return result, outputs, error


@pytest.fixture
def seen():
    return []


@pytest.fixture
def make_client(seen):
    def make(execute=None, **options):
        async def echo(context):
            seen.append(context)
            return {"echo": context.input("message")}

        config = Config(port=5110, mycelium_url="https://mycelium.test", token="tok")
        node = NodeService(execute or echo, PORTS, **options)
        host = ServiceHost("Example", config, node.handle)
        node.mount(host)
        with patch.object(host.mycelium, "register", new=AsyncMock(return_value=True)):
            with TestClient(host.app) as client:
                client.host = host
                yield client

    return make


@pytest.fixture
def client(make_client):
    yield from make_client()


def run(client, **fields):
    return client.post("/handle", json={**ENVELOPE, **fields})


def test_a_successful_run_answers_result_success_with_the_outputs(client):
    res = run(client, inputs={"message": "hello"})
    assert res.status_code == 200
    assert phloem_reads(res.json()) == ("success", {"echo": "hello"}, None)


def test_the_service_gets_the_run_the_node_its_params_and_its_inputs(client, seen):
    run(client, params={"mode": "fast"}, inputs={"message": "hi"})
    context = seen[0]
    assert context.run_id == uuid.UUID(RUN_ID)
    assert context.node_id == uuid.UUID(NODE_ID)
    assert context.params == {"mode": "fast"}
    assert context.inputs == {"message": "hi"}
    assert context.param("mode") == "fast"
    assert context.param("missing") is None


def test_a_run_not_split_over_items_has_no_index_and_is_not_a_rehearsal(client, seen):
    run(client, inputs={"message": "hi"})
    assert seen[0].index is None
    assert seen[0].rehearsal is False


def test_the_index_of_a_fanned_out_item_and_a_rehearsal_are_passed_on(client, seen):
    run(client, inputs={"message": "hi"}, index=3, rehearsal=True)
    assert seen[0].index == 3
    assert seen[0].rehearsal is True


def test_an_index_that_is_null_is_no_index(client, seen):
    run(client, inputs={"message": "hi"}, index=None)
    assert seen[0].index is None


def test_an_exception_is_a_fail_answer_carrying_its_message_not_a_server_error(make_client):
    async def refuses(context):
        raise ValueError("the raster is not there")

    for client in make_client(refuses):
        res = run(client)
        assert res.status_code == 200
        assert phloem_reads(res.json()) == ("fail", {}, "the raster is not there")


def test_an_exception_with_no_message_is_a_fail_answer_naming_its_type(make_client):
    async def refuses(context):
        raise KeyError()

    for client in make_client(refuses):
        assert phloem_reads(run(client).json())[2] == "KeyError"


def test_outputs_that_are_not_an_object_are_a_fail_answer(make_client):
    async def wrong(context):
        return ["not", "an", "object"]

    for client in make_client(wrong):
        result, _, error = phloem_reads(run(client).json())
        assert result == "fail"
        assert "outputs" in error


def test_a_service_with_nothing_to_answer_succeeds_with_no_outputs(make_client):
    async def nothing(context):
        return None

    for client in make_client(nothing):
        assert phloem_reads(run(client).json()) == ("success", {}, None)


def test_a_plain_function_is_run_off_the_event_loop_so_a_slow_read_does_not_hold_up_health(make_client):
    on_the_loop = []

    def blocking(context):
        try:
            asyncio.get_running_loop()
            on_the_loop.append(True)
        except RuntimeError:
            on_the_loop.append(False)
        return {"thread": threading.current_thread().name}

    for client in make_client(blocking):
        assert phloem_reads(run(client).json())[0] == "success"
    assert on_the_loop == [False]


def test_a_plain_function_that_raises_is_a_fail_answer(make_client):
    def blocking(context):
        raise RuntimeError("could not read")

    for client in make_client(blocking):
        assert phloem_reads(run(client).json()) == ("fail", {}, "could not read")


@pytest.mark.parametrize("field, value", [("runId", "not-a-guid"), ("nodeId", 7), ("index", "three"), ("index", True), ("rehearsal", "yes")])
def test_an_envelope_with_a_malformed_field_is_a_fail_answer_saying_so(client, field, value):
    res = run(client, **{field: value})
    assert res.status_code == 200
    result, _, error = phloem_reads(res.json())
    assert result == "fail"
    assert error.startswith("Malformed node envelope:")
    assert field in error


@pytest.mark.parametrize("body", [
    {"relationshipId": "r", "subjectId": "s", "targetId": "t", "properties": {}},
    {"runId": RUN_ID},
    {"nodeId": NODE_ID},
    ["runId", "nodeId"],
])
def test_a_body_that_is_not_a_node_envelope_is_refused_with_what_is_expected(client, body):
    res = client.post("/handle", json=body)
    assert res.status_code == 400
    assert "runId" in res.json()["error"] and "nodeId" in res.json()["error"]


def test_params_and_inputs_that_are_not_objects_read_as_empty(client, seen):
    run(client, params="x", inputs=[1])
    assert seen[0].params == {} and seen[0].inputs == {}


def test_a_literal_input_passes_through_and_so_does_a_ref_that_is_not_well_formed(client, seen):
    malformed = {"ref": {"thingId": "not-a-guid", "property": "mass"}}
    no_property = {"ref": {"thingId": THING}}
    run(client, inputs={"a": 1, "b": malformed, "c": no_property, "d": {"other": "shape"}})
    assert seen[0].inputs == {"a": 1, "b": malformed, "c": no_property, "d": {"other": "shape"}}


def test_a_graph_reference_input_is_replaced_by_the_propertys_current_value(client, seen):
    properties = {"Mass": {"Value": 42.5, "TypeInfo": "vos.Number"}, "Other": {"Value": 1}}
    with patch.object(client.host.mycelium, "thing_properties", new=AsyncMock(return_value=properties)) as read:
        res = run(client, inputs={"message": {"ref": {"thingId": THING, "property": "mass"}}})
    read.assert_awaited_once_with(uuid.UUID(THING))
    assert seen[0].inputs == {"message": 42.5}
    assert phloem_reads(res.json())[0] == "success"


def test_a_graph_reference_to_a_property_with_no_value_wrapper_is_read_as_it_is(client, seen):
    with patch.object(client.host.mycelium, "thing_properties", new=AsyncMock(return_value={"Name": "Weir"})):
        run(client, inputs={"message": {"ref": {"thingId": THING, "property": "name"}}})
    assert seen[0].inputs == {"message": "Weir"}


def test_each_reference_is_resolved_and_literals_beside_them_are_kept(client, seen):
    with patch.object(client.host.mycelium, "thing_properties", new=AsyncMock(return_value={"A": {"Value": 1}})):
        run(client, inputs={"x": {"ref": {"thingId": THING, "property": "A"}}, "y": "literal"})
    assert seen[0].inputs == {"x": 1, "y": "literal"}


def test_a_reference_to_a_property_the_thing_does_not_have_fails_the_run_and_the_service_is_not_called(client, seen):
    with patch.object(client.host.mycelium, "thing_properties", new=AsyncMock(return_value={"A": {"Value": 1}})):
        result, _, error = phloem_reads(run(client, inputs={"message": {"ref": {"thingId": THING, "property": "Missing"}}}).json())
    assert result == "fail"
    assert "Missing" in error and THING in error
    assert seen == []


def test_a_reference_that_cannot_be_read_fails_the_run_naming_it(client, seen):
    unreadable = AsyncMock(side_effect=RuntimeError("returned 404"))
    with patch.object(client.host.mycelium, "thing_properties", new=unreadable):
        result, _, error = phloem_reads(run(client, inputs={"message": {"ref": {"thingId": THING, "property": "mass"}}}).json())
    assert result == "fail"
    assert error.startswith("Failed to resolve input ref") and THING in error and "404" in error
    assert seen == []


def test_the_manifest_lists_the_ports_and_by_default_does_not_rehearse(client):
    manifest = client.get("/manifest").json()
    assert manifest == {
        "service": "Example",
        "rehearses": False,
        "ports": [
            {"portName": "message", "direction": "in", "type": "string", "required": True},
            {"portName": "echo", "direction": "out", "type": "string", "required": False},
        ],
        "settings": [],
    }


def test_the_manifest_says_whether_the_service_rehearses_and_lists_its_settings(make_client):
    settings = [
        Setting("operation", required=True, values=["metadata", "point"]),
        Setting("resampling", values=["nearest", "bilinear"], default="nearest"),
        Setting("note"),
    ]
    for client in make_client(rehearses=True, settings=settings):
        manifest = client.get("/manifest").json()
    assert manifest["rehearses"] is True
    assert manifest["settings"] == [
        {"name": "operation", "required": True, "values": ["metadata", "point"]},
        {"name": "resampling", "required": False, "values": ["nearest", "bilinear"], "default": "nearest"},
        {"name": "note", "required": False},
    ]


def test_the_manifest_names_the_service_it_is_served_for(client):
    assert client.get("/manifest").json()["service"] == client.host.service_name


def test_the_manifest_needs_a_token_once_a_key_is_configured_as_it_does_in_the_c_sharp_services(client):
    pair = ec.generate_private_key(ec.SECP256R1())
    config = client.host.config
    config.verification_key = base64.b64encode(pair.public_key().public_bytes(Encoding.DER, PublicFormat.SubjectPublicKeyInfo)).decode()
    config.issuer, config.audience = "VillageOS", "example"
    assert client.get("/manifest").status_code == 401
    now = int(time.time())
    token = jwt.encode({"iss": "VillageOS", "aud": "example", "sub": "mycelium", "iat": now, "nbf": now, "exp": now + 60}, pair, algorithm="ES256")
    assert client.get("/manifest", headers={"Authorization": f"Bearer {token}"}).status_code == 200


def test_handle_still_requires_a_token_once_a_key_is_configured(client):
    pair = ec.generate_private_key(ec.SECP256R1())
    config = client.host.config
    config.verification_key = base64.b64encode(pair.public_key().public_bytes(Encoding.DER, PublicFormat.SubjectPublicKeyInfo)).decode()
    config.issuer, config.audience = "VillageOS", "example"
    assert run(client).status_code == 401
