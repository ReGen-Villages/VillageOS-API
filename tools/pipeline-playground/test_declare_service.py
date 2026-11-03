"""Spec for declaring one service in a seed from its manifest: the service, its connection, its ports and the
vocabulary the orchestrator requires come out of the manifest plus the deployment's subdomain, port and
program; a published pipeline runs it; a seed that already declares the service keeps its own Things; a second
run changes nothing; --remove takes it back out."""
import io
import json
import os
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

import declare_service as D
import generate as G

HERE = os.path.dirname(os.path.abspath(__file__))

RASTER_MANIFEST = {
    "service": "Raster reader",
    "rehearses": True,
    "ports": [
        {"portName": "raster", "direction": "in", "type": "string", "required": True},
        {"portName": "lon", "direction": "in", "type": "number", "required": False},
        {"portName": "bbox", "direction": "in", "type": "array"},
        {"portName": "result", "direction": "out", "type": "object", "required": False},
    ],
    "settings": [
        {"name": "operation", "required": True, "values": ["metadata", "point", "zonal"]},
        {"name": "resampling", "values": ["nearest", "bilinear"]},
        {"name": "retries", "default": 2},
    ],
}


def value(thing, name):
    return D.property_value(thing, name)


def things_named(seed, name):
    return [t for t in seed["Things"] if t["Name"] == name]


def connection_named(seed, name):
    """The connection and the node running it share the service's name; the connection is the one reached at a subdomain."""
    [connection] = [t for t in things_named(seed, name) if value(t, "Subdomain")]
    return connection


def rels_of(seed, subject=None, predicate_name=None, target=None):
    names = {t["Id"]: t["Name"] for t in seed["Things"]}
    return [r for r in seed["Relationships"]
            if (subject is None or r["Subject"] == subject)
            and (predicate_name is None or names.get(r["Predicate"]) == predicate_name)
            and (target is None or r["Target"] == target)]


def bound_in_seed(seed, word):
    binds = {t["Id"] for t in seed["Things"] if t["Name"] == "binds"}
    return [r["Target"] for r in seed["Relationships"] if r["Subject"] == G.role_id(word) and r["Predicate"] in binds]


def declared_into(seed, manifest=RASTER_MANIFEST, subdomain="raster", port=5110, program="/srv/raster/app.py", name=None):
    parsed = D.parse_manifest(manifest)
    kit, declared = D.build(parsed, subdomain, name or parsed["name"] or subdomain, port, program)
    found = D.already_declared(seed, declared, subdomain)
    counts = G.merge_into(kit, seed, found)
    return kit, declared, found, counts


def validate(seed):
    with tempfile.TemporaryDirectory() as scratch:
        path = os.path.join(scratch, "declared.seed.json")
        G.dump_seed(seed, path)
        return subprocess.run([sys.executable, "-I", os.path.join(HERE, "validate_fragment.py"), path],
                              capture_output=True, text=True)


class ManifestTests(unittest.TestCase):
    def test_an_object_manifest_is_read_with_its_name_rehearsal_ports_and_settings(self):
        manifest = D.parse_manifest(RASTER_MANIFEST)
        self.assertEqual(manifest["name"], "Raster reader")
        self.assertTrue(manifest["rehearses"])
        self.assertEqual([(p["portName"], p["direction"], p["type"], p["required"], p["collection"]) for p in manifest["ports"]],
                         [("raster", "in", "string", True, False), ("lon", "in", "number", False, False),
                          ("bbox", "in", "array", False, False), ("result", "out", "object", False, False)])
        self.assertEqual([s["name"] for s in manifest["settings"]], ["operation", "resampling", "retries"])

    def test_a_bare_list_of_ports_is_a_manifest_that_names_nothing_and_does_not_rehearse(self):
        manifest = D.parse_manifest([{"portName": "message", "direction": "in", "type": "string", "required": True},
                                     {"portName": "echo", "direction": "out", "type": "string"}])
        self.assertIsNone(manifest["name"])
        self.assertFalse(manifest["rehearses"])
        self.assertEqual([p["portName"] for p in manifest["ports"]], ["message", "echo"])
        self.assertEqual(manifest["settings"], [])

    def test_a_port_without_a_direction_is_refused(self):
        with self.assertRaises(D.ManifestError):
            D.parse_manifest({"ports": [{"portName": "raster", "type": "string"}]})

    def test_a_manifest_without_ports_is_refused(self):
        with self.assertRaises(D.ManifestError):
            D.parse_manifest({"service": "Raster reader"})

    def test_a_setting_without_a_name_is_refused(self):
        with self.assertRaises(D.ManifestError):
            D.parse_manifest({"ports": [], "settings": [{"values": ["a", "b"]}]})

    def test_a_setting_is_written_with_the_type_of_its_value(self):
        self.assertEqual(D.typed_setting(True), {"typeInfo": "vos.Boolean", "value": True})
        self.assertEqual(D.typed_setting(2), {"typeInfo": "vos.LongInteger", "value": 2})
        self.assertEqual(D.typed_setting(0.5), {"typeInfo": "vos.Double", "value": 0.5})
        self.assertEqual(D.typed_setting("nearest"), {"typeInfo": "vos.String", "value": "nearest"})

    def test_a_manifest_is_read_from_a_running_service_as_from_a_file(self):
        answer = io.BytesIO(json.dumps(RASTER_MANIFEST).encode("utf-8"))
        with mock.patch("urllib.request.urlopen", return_value=answer) as opened:
            manifest = D.read_manifest("http://localhost:5110/manifest")
        opened.assert_called_once_with("http://localhost:5110/manifest")
        self.assertEqual(manifest["name"], "Raster reader")
        with tempfile.TemporaryDirectory() as scratch:
            path = os.path.join(scratch, "manifest.json")
            with open(path, "w", encoding="utf-8") as f:
                json.dump(RASTER_MANIFEST, f)
            self.assertEqual(D.read_manifest(path), manifest)

    def test_a_setting_takes_its_default_then_its_first_allowed_value_when_required_and_is_otherwise_empty(self):
        manifest = D.parse_manifest(RASTER_MANIFEST)
        by_name = {s["name"]: s for s in manifest["settings"]}
        self.assertEqual(D.setting_value(by_name["operation"]), "metadata")
        self.assertEqual(D.setting_value(by_name["resampling"]), "")
        self.assertEqual(D.setting_value(by_name["retries"]), 2)


class EmptySeedTests(unittest.TestCase):
    """A seed holding nothing of the service comes out with everything the orchestrator reads."""

    def setUp(self):
        G.set_namespace(None)
        self.seed = {"Name": "Target", "Things": [], "Relationships": []}
        self.kit, self.declared, self.found, _ = declared_into(self.seed)

    def test_nothing_was_found(self):
        self.assertEqual(self.found, {})

    def test_the_service_carries_its_program_port_and_rehearsal_and_is_what_the_service_role_binds(self):
        [service] = things_named(self.seed, "Raster reader service")
        self.assertEqual(value(service, "ExecutablePath"), "/srv/raster/app.py")
        self.assertEqual(service["Properties"]["ServicePort"], {"typeInfo": "vos.LongInteger", "value": 5110})
        self.assertEqual(service["Properties"]["rehearses"], {"typeInfo": "vos.Boolean", "value": True})
        [archetype] = bound_in_seed(self.seed, "service")
        self.assertEqual(len(rels_of(self.seed, service["Id"], "is", archetype)), 1)

    def test_the_connection_carries_the_subdomain_holds_the_service_and_is_triggered_over_http(self):
        connection = connection_named(self.seed, "Raster reader")
        self.assertEqual(value(connection, "Subdomain"), "raster")
        [archetype] = bound_in_seed(self.seed, "connection")
        self.assertEqual(len(rels_of(self.seed, connection["Id"], "is", archetype)), 1)
        [service] = things_named(self.seed, "Raster reader service")
        self.assertEqual(len(rels_of(self.seed, connection["Id"], "has", service["Id"])), 1)
        [http] = bound_in_seed(self.seed, "httpTrigger")
        self.assertEqual(len(rels_of(self.seed, connection["Id"], "triggeredBy", http)), 1)

    def test_one_port_thing_per_manifest_port_with_the_manifests_properties(self):
        [service] = things_named(self.seed, "Raster reader service")
        [port_archetype] = bound_in_seed(self.seed, "port")
        held = [r["Target"] for r in rels_of(self.seed, service["Id"], "has")]
        ports = {value(t, "portName"): t for t in self.seed["Things"] if t["Id"] in held}
        self.assertEqual(set(ports), {"raster", "lon", "bbox", "result"})
        for port in ports.values():
            self.assertEqual(len(rels_of(self.seed, port["Id"], "is", port_archetype)), 1)
        self.assertEqual(ports["raster"]["Properties"]["required"], {"typeInfo": "vos.Boolean", "value": True})
        self.assertEqual(value(ports["bbox"], "type"), "array")
        self.assertEqual(value(ports["result"], "direction"), "out")

    def test_the_service_node_carries_each_setting(self):
        [node] = [t for t in things_named(self.seed, "Raster reader") if "operation" in t["Properties"]]
        self.assertEqual(node["Properties"]["operation"], {"typeInfo": "vos.String", "value": "metadata"})
        self.assertEqual(node["Properties"]["resampling"], {"typeInfo": "vos.String", "value": ""})
        self.assertEqual(node["Properties"]["retries"], {"typeInfo": "vos.LongInteger", "value": 2})

    def test_the_pipeline_is_published_and_the_validator_accepts_it(self):
        [pipeline] = things_named(self.seed, "Raster reader pipeline")
        self.assertEqual(pipeline["Properties"]["published"], {"typeInfo": "vos.Boolean", "value": True})
        run = validate(self.seed)
        self.assertEqual(run.returncode, 0, run.stdout + run.stderr)
        self.assertIn("Raster reader pipeline", run.stdout)

    def test_every_role_the_orchestrator_requires_binds_one_thing(self):
        for word in ("connection", "service", "pipeline", "pipelineNode", "pipelineInput", "pipelineOutput", "port",
                     "pipelineWire", "inputConnector", "outputConnector", "takenOn", "success", "fail", "errorPort",
                     "decisionPath", "comparesWith", "httpTrigger", "configuration", "operation", "predicate"):
            self.assertEqual(len(bound_in_seed(self.seed, word)), 1, word)

    def test_the_input_node_fills_each_required_in_port_and_the_output_node_takes_every_out_port_and_the_error(self):
        wires = [t for t in self.seed["Things"] if " to " in t["Name"] and "fromPath" in t["Properties"]]
        self.assertEqual(sorted(t["Name"] for t in wires),
                         ["Input.raster to Raster reader.raster", "Raster reader.error to Output.error",
                          "Raster reader.result to Output.result"])
        [success] = bound_in_seed(self.seed, "success")
        [fail] = bound_in_seed(self.seed, "fail")
        taken = {t["Name"]: [r["Target"] for r in rels_of(self.seed, t["Id"], "takenOn")] for t in wires}
        self.assertEqual(taken["Raster reader.error to Output.error"], [fail])
        self.assertEqual(taken["Raster reader.result to Output.result"], [success])
        self.assertEqual(taken["Input.raster to Raster reader.raster"], [success])
        [error_port] = bound_in_seed(self.seed, "errorPort")
        [error_wire] = [t for t in wires if t["Name"].startswith("Raster reader.error")]
        self.assertEqual([r["Target"] for r in rels_of(self.seed, error_wire["Id"], "inputConnector")], [error_port])

    def test_a_service_with_no_required_in_port_starts_at_its_own_node(self):
        seed = {"Name": "Target", "Things": [], "Relationships": []}
        declared_into(seed, [{"portName": "tick", "direction": "out", "type": "number"}], subdomain="timer")
        self.assertEqual(things_named(seed, "Input"), [])
        self.assertEqual(len(things_named(seed, "Output")), 1)
        run = validate(seed)
        self.assertEqual(run.returncode, 0, run.stdout + run.stderr)

    def test_a_second_run_changes_nothing(self):
        before = json.dumps(self.seed, sort_keys=True)
        _, _, found, counts = declared_into(self.seed)
        self.assertEqual(counts, (0, 0))
        self.assertEqual(json.dumps(self.seed, sort_keys=True), before)
        self.assertEqual(set(found), {self.declared["connection"], self.declared["service"], *self.declared["ports"].values()})

    def test_remove_takes_the_service_its_connection_its_ports_and_the_pipeline_out_and_leaves_the_vocabulary(self):
        G.remove_from(self.kit, self.seed)
        for name in ("Raster reader", "Raster reader service", "Raster reader pipeline", "Input", "Output"):
            self.assertEqual(things_named(self.seed, name), [], name)
        self.assertEqual([t for t in self.seed["Things"] if "portName" in t["Properties"] and value(t, "portName") != "error"], [])
        for word in ("connection", "service", "pipeline", "errorPort"):
            self.assertEqual(len(bound_in_seed(self.seed, word)), 1, word)


def seed_declaring_its_own_service(binding_the_roles=True):
    """A seed declaring a service the way a site seed does: the connection and service archetypes under its
    own names, bound by the roles the platform reads them through; the service's port inherited from a
    prototype; one of the manifest's ports missing."""
    role_things = [{"Id": G.role_id("connection"), "Name": "Connection", "Properties": {}},
                   {"Id": G.role_id("service"), "Name": "Service", "Properties": {}},
                   {"Id": "v-binds", "Name": "binds", "Properties": {}}] if binding_the_roles else []
    bindings = [{"Id": "b1", "Name": "", "Subject": G.role_id("connection"), "Predicate": "v-binds", "Target": "v-connection-kind", "Properties": {}},
                {"Id": "b2", "Name": "", "Subject": G.role_id("service"), "Predicate": "v-binds", "Target": "v-service-kind", "Properties": {}}] if binding_the_roles else []
    return {"Name": "Village", "Things": [
        *role_things,
        {"Id": "v-is", "Name": "is", "Properties": {}},
        {"Id": "v-has", "Name": "has", "Properties": {}},
        {"Id": "v-connection-kind", "Name": "Door", "IsArchetype": True, "Properties": {}},
        {"Id": "v-service-kind", "Name": "Programme", "IsArchetype": True, "Properties": {}},
        {"Id": "v-prototype", "Name": "Raster prototype", "IsArchetype": True,
         "Properties": {"ServicePort": {"typeInfo": "vos.LongInteger", "value": 7110}}},
        {"Id": "v-service", "Name": "The reader", "Properties": {"ExecutablePath": {"typeInfo": "vos.String", "value": "/opt/reader"}}},
        {"Id": "v-connection", "Name": "Reader door", "Properties": {"Subdomain": {"typeInfo": "vos.String", "value": "Raster"}}},
        {"Id": "v-port-raster", "Name": "the raster", "Properties": {"portName": {"typeInfo": "vos.String", "value": "raster"},
                                                                     "direction": {"typeInfo": "vos.String", "value": "in"},
                                                                     "type": {"typeInfo": "vos.String", "value": "string"},
                                                                     "required": {"typeInfo": "vos.Boolean", "value": True}}},
        {"Id": "v-port-result", "Name": "the result", "Properties": {"portName": {"typeInfo": "vos.String", "value": "result"},
                                                                     "direction": {"typeInfo": "vos.String", "value": "out"},
                                                                     "type": {"typeInfo": "vos.String", "value": "object"}}},
        {"Id": "v-http", "Name": "http", "Properties": {}},
    ], "Relationships": [
        *bindings,
        {"Id": "r1", "Name": "", "Subject": "v-connection", "Predicate": "v-is", "Target": "v-connection-kind", "Properties": {}},
        {"Id": "r2", "Name": "", "Subject": "v-connection", "Predicate": "v-has", "Target": "v-service", "Properties": {}},
        {"Id": "r3", "Name": "", "Subject": "v-service", "Predicate": "v-is", "Target": "v-prototype", "Properties": {}},
        {"Id": "r4", "Name": "", "Subject": "v-prototype", "Predicate": "v-is", "Target": "v-service-kind", "Properties": {}},
        {"Id": "r5", "Name": "", "Subject": "v-prototype", "Predicate": "v-has", "Target": "v-port-raster", "Properties": {}},
        {"Id": "r6", "Name": "", "Subject": "v-service", "Predicate": "v-has", "Target": "v-port-result", "Properties": {}},
    ]}


class SeedAlreadyDeclaringTheServiceTests(unittest.TestCase):
    def setUp(self):
        G.set_namespace(None)
        self.seed = seed_declaring_its_own_service()
        self.before = {t["Id"]: json.dumps(t, sort_keys=True) for t in self.seed["Things"]}
        self.kit, self.declared, self.found, _ = declared_into(self.seed, port=5110, program="/srv/raster/app.py")

    def test_the_connection_is_found_by_its_subdomain_whatever_its_case_the_service_as_what_it_holds_and_each_port_by_name_and_direction(self):
        self.assertEqual(self.found[self.declared["connection"]], "v-connection")
        self.assertEqual(self.found[self.declared["service"]], "v-service")
        self.assertEqual(self.found[self.declared["ports"][("raster", "in")]], "v-port-raster")
        self.assertEqual(self.found[self.declared["ports"][("result", "out")]], "v-port-result")
        self.assertNotIn(self.declared["ports"][("lon", "in")], self.found)

    def test_the_seeds_own_things_are_kept_as_they_were(self):
        for thing in self.seed["Things"]:
            if thing["Id"] in self.before:
                self.assertEqual(json.dumps(thing, sort_keys=True), self.before[thing["Id"]], thing["Name"])
        self.assertEqual(things_named(self.seed, "Raster reader"), [t for t in things_named(self.seed, "Raster reader") if "operation" in t["Properties"]])
        self.assertEqual(things_named(self.seed, "Raster reader service"), [])
        self.assertEqual([r for r in self.seed["Relationships"] if r["Subject"] == "v-connection" and r["Id"] not in ("r1", "r2")], [])

    def test_the_ports_the_seed_lacks_are_added_to_its_own_service(self):
        added = [r["Target"] for r in rels_of(self.seed, "v-service", "has") if r["Target"] != "v-port-result"]
        names = {value(t, "portName") for t in self.seed["Things"] if t["Id"] in added}
        self.assertEqual(names, {"lon", "bbox"})

    def test_the_pipeline_runs_the_seeds_own_connection_and_its_wires_reach_the_seeds_own_ports(self):
        [node] = [t for t in things_named(self.seed, "Raster reader") if "operation" in t["Properties"]]
        self.assertEqual([r["Target"] for r in rels_of(self.seed, node["Id"], "has") if r["Target"] == "v-connection"], ["v-connection"])
        wires = {t["Name"]: t for t in self.seed["Things"] if " to " in t["Name"] and "fromPath" in t["Properties"]}
        into_raster = wires["Input.raster to Raster reader.raster"]
        self.assertEqual([r["Target"] for r in rels_of(self.seed, into_raster["Id"], "outputConnector")], ["v-port-raster"])
        from_result = wires["Raster reader.result to Output.result"]
        self.assertEqual([r["Target"] for r in rels_of(self.seed, from_result["Id"], "inputConnector")], ["v-port-result"])

    def test_the_seeds_own_archetypes_keep_playing_the_roles(self):
        self.assertEqual(bound_in_seed(self.seed, "connection"), ["v-connection-kind"])
        self.assertEqual(bound_in_seed(self.seed, "service"), ["v-service-kind"])
        self.assertEqual(things_named(self.seed, "PlatformServiceConnection"), [])

    def test_a_seed_binding_no_role_has_its_connection_and_service_placed_under_the_archetypes_the_roles_come_to_bind(self):
        seed = seed_declaring_its_own_service(binding_the_roles=False)
        declared_into(seed)
        [connection_archetype] = bound_in_seed(seed, "connection")
        [service_archetype] = bound_in_seed(seed, "service")
        self.assertEqual({t["Name"] for t in seed["Things"] if t["Id"] in (connection_archetype, service_archetype)},
                         {"PlatformServiceConnection", "Service"})
        self.assertEqual(len(rels_of(seed, "v-connection", "is", connection_archetype)), 1)
        self.assertEqual(len(rels_of(seed, "v-service", "is", service_archetype)), 1)
        run = validate(seed)
        self.assertEqual(run.returncode, 0, run.stdout + run.stderr)

    def test_remove_leaves_the_seeds_own_things_and_takes_back_what_was_added(self):
        G.remove_from(self.kit, self.seed)
        for tid, text in self.before.items():
            [thing] = [t for t in self.seed["Things"] if t["Id"] == tid]
            self.assertEqual(json.dumps(thing, sort_keys=True), text)
        self.assertEqual(things_named(self.seed, "Raster reader pipeline"), [])
        self.assertEqual([r["Target"] for r in rels_of(self.seed, "v-service", "has")], ["v-port-result"])
        self.assertEqual([r["Id"] for r in self.seed["Relationships"] if r["Id"].startswith("r")], ["r1", "r2", "r3", "r4", "r5", "r6"])

    def test_a_connection_holding_no_service_is_given_the_manifests_service_and_its_ports(self):
        seed = seed_declaring_its_own_service()
        seed["Relationships"] = [r for r in seed["Relationships"] if r["Id"] != "r2"]
        seed["Things"].append({"Id": "v-note", "Name": "a note", "Properties": {}})
        seed["Relationships"].append({"Id": "r7", "Name": "", "Subject": "v-connection", "Predicate": "v-has", "Target": "v-note", "Properties": {}})
        kit, declared, found, _ = declared_into(seed)

        self.assertEqual(found, {declared["connection"]: "v-connection"})
        [service] = things_named(seed, "Raster reader service")
        self.assertEqual([r["Target"] for r in rels_of(seed, "v-connection", "has")], ["v-note", service["Id"]])
        self.assertEqual(len(rels_of(seed, service["Id"], "has")), 4)
        self.assertIn("added service 'Raster reader service'", D.describe(seed, kit, declared, found))

    def test_a_seed_holding_nothing_of_the_service_is_reported_as_all_added(self):
        seed = {"Name": "Target", "Things": [], "Relationships": []}
        kit, declared, found, _ = declared_into(seed)
        lines = D.describe(seed, kit, declared, found)
        self.assertTrue(all(line.startswith("added ") for line in lines), lines)
        self.assertIn("added connection 'Raster reader'", lines)

    def test_what_was_found_and_what_was_added_is_reported(self):
        lines = D.describe(self.seed, self.kit, self.declared, self.found)
        self.assertIn("found connection 'Reader door'", lines)
        self.assertIn("found service 'The reader'", lines)
        self.assertIn("found in port 'raster'", lines)
        self.assertIn("added in port 'lon'", lines)
        self.assertIn("added pipeline 'Raster reader pipeline'", lines)


class CommandLineTests(unittest.TestCase):
    def run_tool(self, *arguments):
        return subprocess.run([sys.executable, "-I", os.path.join(HERE, "declare_service.py"), *arguments],
                              capture_output=True, text=True)

    def test_the_tool_edits_the_seed_in_place_keeps_a_backup_and_reports(self):
        with tempfile.TemporaryDirectory() as scratch:
            manifest = os.path.join(scratch, "manifest.json")
            with open(manifest, "w", encoding="utf-8") as f:
                json.dump(RASTER_MANIFEST, f)
            seed = os.path.join(scratch, "Target.seed.json")
            G.dump_seed({"Name": "Target", "Things": [], "Relationships": []}, seed)

            run = self.run_tool("--manifest", manifest, "--subdomain", "raster", "--port", "5110",
                                "--program", "/srv/raster/app.py", "--into", seed)

            self.assertEqual(run.returncode, 0, run.stdout + run.stderr)
            self.assertIn("added connection 'Raster reader'", run.stdout)
            self.assertIn("Merged into Target", run.stdout)
            self.assertTrue(os.path.exists(seed + ".bak"))
            edited = G.load_seed(seed)
            self.assertEqual(len(things_named(edited, "Raster reader pipeline")), 1)
            validated = subprocess.run([sys.executable, "-I", os.path.join(HERE, "validate_fragment.py"), seed],
                                       capture_output=True, text=True)
            self.assertEqual(validated.returncode, 0, validated.stdout + validated.stderr)

            removed = self.run_tool("--manifest", manifest, "--subdomain", "raster", "--into", seed, "--remove")
            self.assertEqual(removed.returncode, 0, removed.stdout + removed.stderr)
            self.assertEqual(things_named(G.load_seed(seed), "Raster reader pipeline"), [])

    def test_declaring_needs_the_port_and_the_program(self):
        with tempfile.TemporaryDirectory() as scratch:
            manifest = os.path.join(scratch, "manifest.json")
            with open(manifest, "w", encoding="utf-8") as f:
                json.dump(RASTER_MANIFEST, f)
            run = self.run_tool("--manifest", manifest, "--subdomain", "raster", "--into", manifest)
        self.assertEqual(run.returncode, 2)
        self.assertIn("--port and --program", run.stderr)

    def test_two_namespaces_declare_the_service_under_disjoint_ids_but_the_role_things(self):
        parsed = D.parse_manifest(RASTER_MANIFEST)
        G.set_namespace("Alpha")
        try:
            alpha, _ = D.build(parsed, "raster", "Raster reader", 5110, "/srv/raster/app.py")
            G.set_namespace("Beta")
            beta, _ = D.build(parsed, "raster", "Raster reader", 5110, "/srv/raster/app.py")
        finally:
            G.set_namespace(None)
        self.assertEqual(set(alpha.things) & set(beta.things), alpha.role_things | {G.ROLE_ARCHETYPE})


if __name__ == "__main__":
    unittest.main()
