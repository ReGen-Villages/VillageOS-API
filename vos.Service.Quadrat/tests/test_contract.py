"""What the platform reads from Quadrat, pinned from the platform's side (docs/SERVICES.md §16.2).

test_app.py tests what Quadrat does. This file tests what the rest of the platform depends on Quadrat being: a
manifest in the shape the editor and `declare_service.py` read, an answer in the shape Phloem parses, and a manifest
the platform's own declaration tooling can turn into a model Phloem accepts.
"""
import json
import subprocess
import sys
from pathlib import Path

import pytest

from conftest import square

pytestmark = pytest.mark.usefixtures("reads_in_this_process", "readable")

RUN = {"runId": "6d2b0c1e-0000-4000-8000-000000000001", "nodeId": "6d2b0c1e-0000-4000-8000-000000000002"}
PLAYGROUND = Path(__file__).resolve().parents[2] / "tools" / "pipeline-playground"

PORT_TYPES = {"string", "number", "boolean", "object", "array"}
PORT_KEYS = {"portName", "direction", "type", "required"}
SETTING_KEYS = {"name", "required", "values", "default"}


@pytest.fixture
def manifest(client):
    return client.get("/manifest").json()


def zone_inputs(raster):
    return {"raster": raster, "bbox": [0, 0, 10, 10]}


@pytest.fixture
def requests_by_operation(degrees_grid):
    """One valid request for every operation the manifest offers, built from the inputs that operation reads."""
    line = {"type": "LineString", "coordinates": [[0.5, 7.5], [4.5, 7.5]]}
    polygon = {"raster": degrees_grid, "geometry": square(0, 0, 10, 10)}
    return {
        "metadata": [{"raster": degrees_grid}],
        "point": [{"raster": degrees_grid, "lon": 2.5, "lat": 7.5}],
        "zonal": [zone_inputs(degrees_grid), polygon],
        "histogram": [zone_inputs(degrees_grid), polygon],
        "classify": [{**zone_inputs(degrees_grid), "classes": [{"label": "all", "min": 0}]}],
        "profile": [{"raster": degrees_grid, "line": line, "samples": 3}, {"raster": degrees_grid, "line": line, "spacing": 50_000}],
    }


def call(client, operation, inputs, params=None):
    return client.post("/handle", json={**RUN, "params": {"operation": operation, **(params or {})}, "inputs": inputs})


def test_the_manifest_names_the_service_and_says_it_rehearses(manifest):
    assert manifest["service"] == "Quadrat"
    assert manifest["rehearses"] is True


def test_every_port_is_described_the_way_the_editor_reads_it(manifest):
    assert manifest["ports"]
    for port in manifest["ports"]:
        assert set(port) <= PORT_KEYS | {"collection"}, port
        assert {"portName", "direction"} <= set(port), port
        assert port["direction"] in ("in", "out"), port
        assert port["type"] in PORT_TYPES, port
        assert isinstance(port["required"], bool), port
    names = [port["portName"] for port in manifest["ports"]]
    assert len(names) == len(set(names)), "a port is declared once"


def test_the_node_has_one_output_the_answer_is_read_from(manifest):
    assert [p["portName"] for p in manifest["ports"] if p["direction"] == "out"] == ["result"]


def test_every_setting_is_described_the_way_the_declaration_reads_it(manifest):
    for setting in manifest["settings"]:
        assert set(setting) <= SETTING_KEYS, setting
        assert isinstance(setting["name"], str) and isinstance(setting["required"], bool), setting
        assert isinstance(setting["values"], list) and setting["values"], setting
        if "default" in setting:
            assert setting["default"] in setting["values"], setting


def test_the_operation_setting_offers_exactly_the_operations_the_service_answers(manifest, requests_by_operation):
    [operation] = [s for s in manifest["settings"] if s["name"] == "operation"]
    assert operation["required"] is True
    assert sorted(operation["values"]) == sorted(requests_by_operation)


def test_every_input_a_request_uses_is_a_declared_port_and_every_declared_input_is_used(manifest, requests_by_operation):
    declared = {p["portName"] for p in manifest["ports"] if p["direction"] == "in"}
    used = {name for requests in requests_by_operation.values() for inputs in requests for name in inputs}
    # A terrain tile is read by every operation, so no operation's own request lists it.
    assert used | {"tile"} == declared


def test_a_required_input_is_one_every_operation_needs(manifest, requests_by_operation):
    required = {p["portName"] for p in manifest["ports"] if p["direction"] == "in" and p["required"]}
    for operation, requests in requests_by_operation.items():
        for inputs in requests:
            assert required <= set(inputs), f"{operation} runs without a port the manifest says is required"


def test_every_operation_answers_the_envelope_with_only_the_declared_output(client, manifest, requests_by_operation):
    outputs = {p["portName"] for p in manifest["ports"] if p["direction"] == "out"}
    for operation, requests in requests_by_operation.items():
        for inputs in requests:
            res = call(client, operation, inputs)
            assert res.status_code == 200, operation
            body = res.json()
            assert set(body) == {"result", "outputs", "error"}, operation
            assert body["result"] == "success" and body["error"] is None, (operation, body)
            assert set(body["outputs"]) == outputs, operation


def test_a_refusal_is_a_fail_answer_that_says_its_code_and_carries_no_outputs(client, degrees_grid):
    res = call(client, "zonal", {"raster": degrees_grid, "geometry": square(50, 50, 60, 60)})
    body = res.json()
    assert res.status_code == 200
    assert body["result"] == "fail" and body["outputs"] == {}
    code, _, reason = body["error"].partition(": ")
    assert code == "outside_raster" and reason


def test_a_failure_in_the_service_is_a_fail_answer_not_an_error_status(client):
    res = call(client, "metadata", {"raster": "no-such-file.tif"})
    assert res.status_code == 200
    assert res.json()["result"] == "fail"


@pytest.mark.parametrize("body", [
    {}, {"inputs": {}}, {"runId": RUN["runId"]}, {"nodeId": RUN["nodeId"]}, {"message": "hello"},
])
def test_a_body_without_both_runId_and_nodeId_is_not_a_node_invocation(client, body):
    assert client.post("/handle", json=body).status_code == 400


def test_a_rehearsal_is_answered_like_a_run(client, degrees_grid):
    res = client.post("/handle", json={**RUN, "rehearsal": True, "params": {"operation": "metadata"}, "inputs": {"raster": degrees_grid}})
    assert res.status_code == 200 and res.json()["result"] == "success"


def test_the_platforms_declaration_tooling_turns_the_manifest_into_a_model_phloem_accepts(manifest, tmp_path):
    saved, seed = tmp_path / "quadrat.manifest.json", tmp_path / "model.seed.json"
    saved.write_text(json.dumps(manifest))

    def run(script, *args):
        done = subprocess.run([sys.executable, str(PLAYGROUND / script), *args], capture_output=True, text=True, cwd=PLAYGROUND)
        assert done.returncode == 0, f"{script} failed:\n{done.stdout}\n{done.stderr}"
        return done.stdout

    run("generate.py", "--out", str(seed))
    run("declare_service.py", "--manifest", str(saved), "--subdomain", "quadrat", "--port", "5110", "--program", "app.py",
        "--into", str(seed), "--no-backup")
    run("validate_fragment.py", str(seed))

    things = json.loads(seed.read_text(encoding="utf-8"))["Things"]

    def properties(thing):
        return {name: prop.get("value") for name, prop in thing.get("Properties", {}).items()}

    ports = [properties(thing) for thing in things if thing["Name"].startswith(("quadrat.in.", "quadrat.out."))]
    declared = {(p["portName"], p["direction"], p["type"], p["required"]) for p in ports}
    wanted = {(p["portName"], p["direction"], p["type"], p["required"]) for p in manifest["ports"]}
    assert wanted <= declared, f"the declaration leaves out {sorted(wanted - declared)}"
    nodes = [p for p in map(properties, things) if "operation" in p]
    assert nodes and all(setting["name"] in nodes[0] for setting in manifest["settings"]), "the node carries every setting the manifest lists"
