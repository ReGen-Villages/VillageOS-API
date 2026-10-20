"""Spec for the playground generator's id derivation and for the roles its archetypes and predicates play.
--namespace makes the same DAGs disjoint per model so two seeds can coexist in one broker; the default (no
namespace) keeps the shared standalone identity; merge reconciles the built-in predicates against a target by
name and a Thing playing a role by what the target's role Thing binds, so a target that renamed one keeps its
own.
"""
import unittest

import generate as G


def build_with_namespace(name):
    G.set_namespace(name)
    try:
        return G.build()
    finally:
        G.set_namespace(None)  # never leak the namespace into another test


def bound_by(things, relationships, word):
    """The Things the role Thing for `word` binds, read the way the editor and the orchestrator read them."""
    binds = {t["Id"] for t in things if t["Name"] == "binds"}
    return [r["Target"] for r in relationships
            if r["Subject"] == G.role_id(word) and r["Predicate"] in binds]


def bound_in_seed(seed, word):
    return bound_by(seed["Things"], seed["Relationships"], word)


def value(properties, name):
    entry = (properties or {}).get(name)
    return entry["value"] if isinstance(entry, dict) else ""


EVERY_ROLE = set(G.ARCHETYPE_ROLE.values()) | set(G.PREDICATE_ROLE.values()) | set(G.ROOT_ROLE.values())


class NamespaceTests(unittest.TestCase):
    def test_two_namespaces_share_no_thing_ids_but_the_role_things(self):
        alpha = build_with_namespace("Alpha")
        beta = build_with_namespace("Beta")
        self.assertTrue(alpha.things and beta.things)
        self.assertEqual(set(alpha.things) & set(beta.things), {G.role_id(word) for word in EVERY_ROLE},
                         "namespaced builds share only the role Things, which every model declares alike")

    def test_same_namespace_is_stable(self):
        first = build_with_namespace("Alpha")
        second = build_with_namespace("Alpha")
        self.assertEqual(set(first.things), set(second.things))
        self.assertEqual(set(first.rels), set(second.rels))

    def test_namespaced_ids_differ_from_the_default_root(self):
        G.set_namespace(None)
        default = G.build()
        namespaced = build_with_namespace("Alpha")
        self.assertEqual(set(default.things) & set(namespaced.things), {G.role_id(word) for word in EVERY_ROLE})

    def test_merge_reconciles_shared_vocabulary_by_name_regardless_of_namespace(self):
        # A target that already has `is` keeps its own id; the namespaced kit's `is` reconciles onto it
        # rather than adding a second `is`.
        seed = {"Name": "Target",
                "Things": [{"Id": "target-is", "Name": "is", "Properties": {}}],
                "Relationships": []}
        G.set_namespace("Alpha")
        try:
            kit = G.build()
            G.merge_into(kit, seed)
        finally:
            G.set_namespace(None)
        is_things = [t for t in seed["Things"] if t["Name"] == "is"]
        self.assertEqual(len(is_things), 1)
        self.assertEqual(is_things[0]["Id"], "target-is")


class RoleBindingTests(unittest.TestCase):
    """What the editor and the orchestrator read: a role Thing binds what plays it, and a merge finds the
    target's archetype through that binding rather than by the name this generator happens to use."""

    def test_every_role_binds_one_thing_and_nothing_carries_a_mark(self):
        kit = G.build()
        things, rels = list(kit.things.values()), list(kit.rels.values())
        for word in EVERY_ROLE:
            self.assertEqual(len(bound_by(things, rels, word)), 1, word)
            self.assertEqual(kit.things[G.role_id(word)]["Name"], f"{word} role")
        self.assertEqual([(t["Name"], name) for t in things for name in t["Properties"] if name.startswith("__Is")], [])

    def test_a_role_thing_has_the_standard_identifier_of_its_word(self):
        self.assertEqual(G.role_id("pipeline"), "f8bc9986-6eef-54d9-9940-4aca2497109b")

    def test_merge_reuses_a_renamed_archetype_the_target_binds(self):
        seed = {"Name": "Target",
                "Things": [{"Id": "target-binds", "Name": "binds", "Properties": {}},
                           {"Id": G.role_id("pipeline"), "Name": "the target's pipeline role", "Properties": {}},
                           {"Id": "target-pipeline", "Name": "Workflow", "Properties": {}}],
                "Relationships": [{"Id": "target-binding", "Name": "", "Subject": G.role_id("pipeline"),
                                   "Predicate": "target-binds", "Target": "target-pipeline", "Properties": {}}]}
        G.merge_into(G.build(), seed)

        self.assertEqual(bound_in_seed(seed, "pipeline"), ["target-pipeline"])
        self.assertEqual([t["Name"] for t in seed["Things"] if t["Id"] == G.role_id("pipeline")],
                         ["the target's pipeline role"])
        self.assertEqual([t for t in seed["Things"] if t["Name"] == "Pipeline"], [])
        self.assertTrue(any(r["Target"] == "target-pipeline" for r in seed["Relationships"]
                            if r["Id"] != "target-binding"),
                        "the playground's pipelines must `is` the target's own archetype")

    def test_merging_twice_leaves_one_binding_per_role_and_adds_nothing(self):
        seed = {"Name": "Target", "Things": [], "Relationships": []}
        G.merge_into(G.build(), seed)
        after_first = (len(seed["Things"]), len(seed["Relationships"]))
        G.merge_into(G.build(), seed)

        self.assertEqual((len(seed["Things"]), len(seed["Relationships"])), after_first)
        for word in EVERY_ROLE:
            self.assertEqual(len(bound_in_seed(seed, word)), 1, f"the {word} role must bind exactly one Thing")

    def test_remove_leaves_the_target_its_role_things_and_what_they_bind(self):
        seed = {"Name": "Target", "Things": [], "Relationships": []}
        G.merge_into(G.build(), seed)
        G.remove_from(G.build(), seed)

        for word in EVERY_ROLE:
            self.assertEqual(len(bound_in_seed(seed, word)), 1, word)

    def test_merge_binds_an_archetype_the_target_holds_by_name_but_never_bound(self):
        seed = {"Name": "Target",
                "Things": [{"Id": "target-pipeline", "Name": "Pipeline", "Properties": {}}],
                "Relationships": []}
        G.merge_into(G.build(), seed)

        self.assertEqual(bound_in_seed(seed, "pipeline"), ["target-pipeline"])


if __name__ == "__main__":
    unittest.main()


class CatalystVocabularyTests(unittest.TestCase):
    """The rails of the Pipeline page read external systems, kinds of message and what a boundary node
    stands for through the roles that bind them, so the kit declares each once and relates through the
    predicates those roles bind."""

    def setUp(self):
        G.set_namespace(None)
        self.kit = G.build()

    def bound(self, word):
        return bound_by(list(self.kit.things.values()), list(self.kit.rels.values()), word)

    def test_a_predicate_playing_a_role_reconciles_onto_a_target_that_holds_it_by_name(self):
        seed = {"Name": "Target",
                "Things": [{"Id": "target-stands-for", "Name": "standsFor", "Properties": {}}],
                "Relationships": []}
        G.merge_into(self.kit, seed)
        stands_for = [t for t in seed["Things"] if t["Name"] == "standsFor"]
        self.assertEqual([t["Id"] for t in stands_for], ["target-stands-for"])
        self.assertEqual(bound_in_seed(seed, "standsFor"), ["target-stands-for"])

    def test_every_sent_kind_arrives_at_a_door_and_every_told_kind_is_a_kind(self):
        [sends] = self.bound("sends")
        [told] = self.bound("told")
        [arrives_at] = self.bound("arrivesAt")
        [kind_archetype] = self.bound("messageKind")
        [is_] = [tid for tid, t in self.kit.things.items() if t["Name"] == "is"]
        rels = list(self.kit.rels.values())
        kinds = {r["Subject"] for r in rels if r["Predicate"] == is_ and r["Target"] == kind_archetype}
        sent = [r["Target"] for r in rels if r["Predicate"] == sends]
        told_kinds = [r["Target"] for r in rels if r["Predicate"] == told]
        self.assertTrue(sent and told_kinds)
        self.assertTrue(set(sent + told_kinds) <= kinds)
        for kind in sent:
            doors = [r["Target"] for r in rels if r["Subject"] == kind and r["Predicate"] == arrives_at]
            self.assertEqual(len(doors), 1, self.kit.things[kind]["Name"])
            self.assertIn("Subdomain", self.kit.things[doors[0]]["Properties"])

    def triggers_of(self, connection):
        [triggered_by] = self.bound("trigger")
        return [self.kit.things[r["Target"]]["Name"] for r in self.kit.rels.values()
                if r["Subject"] == connection and r["Predicate"] == triggered_by]

    def told_through(self):
        [told_through] = self.bound("toldThrough")
        return {r["Subject"]: r["Target"] for r in self.kit.rels.values() if r["Predicate"] == told_through}

    def test_every_service_connection_is_reached_over_http(self):
        [connection_archetype] = self.bound("connection")
        [is_] = [tid for tid, t in self.kit.things.items() if t["Name"] == "is"]
        connections = {r["Subject"] for r in self.kit.rels.values() if r["Predicate"] == is_ and r["Target"] == connection_archetype}
        for connection in connections - set(self.told_through().values()):
            self.assertEqual(self.triggers_of(connection), ["http"], self.kit.things[connection]["Name"])

    def test_a_system_that_is_told_names_a_connection_written_along_and_bound_to_a_service(self):
        [told] = self.bound("told")
        [has] = [tid for tid, t in self.kit.things.items() if t["Name"] == "has"]
        told_systems = {r["Subject"] for r in self.kit.rels.values() if r["Predicate"] == told}
        through = self.told_through()
        self.assertEqual(set(through), told_systems)
        for system, connection in through.items():
            self.assertEqual(self.triggers_of(connection), ["graph"], self.kit.things[system]["Name"])
            bound = [r for r in self.kit.rels.values() if r["Subject"] == connection and r["Predicate"] == has]
            self.assertEqual(len(bound), 1, self.kit.things[system]["Name"])

    def test_a_node_binding_inputs_to_run_parameters_spells_the_property_in_full(self):
        bound = [t for t in self.kit.things.values() if value(t.get("Properties"), "parameterBindings")]
        self.assertGreater(len(bound), 0)
        self.assertEqual([t for t in self.kit.things.values() if value(t.get("Properties"), "paramBindings")], [])

    def test_the_decision_node_holds_its_own_out_ports(self):
        decide = G.stable_id(G._TAG, "node", "Branching Decision", "decide")
        [port_archetype] = self.bound("port")
        has = next(tid for tid, t in self.kit.things.items() if t["Name"] == "has")
        is_ = next(tid for tid, t in self.kit.things.items() if t["Name"] == "is")
        held = [r["Target"] for r in self.kit.rels.values() if r["Subject"] == decide and r["Predicate"] == has
                and any(p["Subject"] == r["Target"] and p["Predicate"] == is_ and p["Target"] == port_archetype
                        for p in self.kit.rels.values())]
        ports = {(value(self.kit.things[p]["Properties"], "portName"), value(self.kit.things[p]["Properties"], "direction"))
                 for p in held}
        self.assertEqual(ports, {("accept", "out"), ("refuse", "out")})

    def test_the_strategy_port_offers_three_things_and_the_node_chose_one_of_them(self):
        [offers] = self.bound("choice")
        port = G.stable_id(G._TAG, "port", "decide", "strategy", "in")
        decide = G.stable_id(G._TAG, "node", "Branching Decision", "decide")
        offered = {r["Target"] for r in self.kit.rels.values() if r["Subject"] == port and r["Predicate"] == offers}
        chosen = {r["Target"] for r in self.kit.rels.values() if r["Subject"] == decide and r["Predicate"] == offers}
        self.assertEqual({self.kit.things[t]["Name"] for t in offered}, {"Nearest first", "Cheapest first", "Soonest first"})
        self.assertEqual({self.kit.things[t]["Name"] for t in chosen}, {"Cheapest first"})
        self.assertTrue(chosen <= offered)

    def wires(self):
        [wire_archetype] = self.bound("pipelineWire")
        is_ = next(tid for tid, t in self.kit.things.items() if t["Name"] == "is")
        has = next(tid for tid, t in self.kit.things.items() if t["Name"] == "has")
        held = {r["Target"] for r in self.kit.rels.values() if r["Predicate"] == has}
        return [r["Subject"] for r in self.kit.rels.values()
                if r["Predicate"] == is_ and r["Target"] == wire_archetype and r["Subject"] in held]

    def connected(self, wire, word):
        [connector] = self.bound(word)
        return [r["Target"] for r in self.kit.rels.values() if r["Subject"] == wire and r["Predicate"] == connector]

    def test_every_wire_relates_to_one_out_port_and_one_in_port_and_names_neither(self):
        wires = self.wires()
        self.assertGreater(len(wires), 0)
        for wire in wires:
            [out_port] = self.connected(wire, "inputConnector")
            [in_port] = self.connected(wire, "outputConnector")
            self.assertEqual(value(self.kit.things[out_port]["Properties"], "direction"), "out")
            self.assertEqual(value(self.kit.things[in_port]["Properties"], "direction"), "in")
            properties = self.kit.things[wire]["Properties"]
            self.assertNotIn("fromPort", properties)
            self.assertNotIn("toPort", properties)
            self.assertEqual(set(properties), {"fromPath", "toPath", "transform"})

    def test_no_wire_is_drawn_as_a_relationship_between_two_nodes(self):
        [wire_archetype] = self.bound("pipelineWire")
        is_ = next(tid for tid, t in self.kit.things.items() if t["Name"] == "is")
        carrying = {r["Subject"] for r in self.kit.rels.values() if r["Predicate"] == is_ and r["Target"] == wire_archetype}
        nodes = {G.stable_id(G._TAG, "node", pipe["name"], node["key"]) for pipe in G.PIPELINES for node in pipe["nodes"]}
        self.assertEqual([r["Name"] for r in self.kit.rels.values() if r["Predicate"] in carrying and r["Subject"] in nodes], [])

    def test_the_text_studio_wire_from_uppercase_takes_its_value_from_the_out_port_named_like_its_in_port(self):
        upper = G.stable_id(G._TAG, "node", "Text Studio", "upper")
        has = next(tid for tid, t in self.kit.things.items() if t["Name"] == "has")
        [wire] = [r["Target"] for r in self.kit.rels.values() if r["Subject"] == upper and r["Predicate"] == has
                  and r["Target"] in self.wires()]
        self.assertEqual(self.connected(wire, "inputConnector"), [G.stable_id(G._TAG, "port", "uppercase", "text", "out")])
        self.assertEqual(self.connected(wire, "outputConnector"), [G.stable_id(G._TAG, "port", "reverse", "text", "in")])

    def test_the_fragment_validator_refuses_a_wire_taking_its_value_from_an_in_port(self):
        [connector] = self.bound("inputConnector")
        fragment = G.as_fragment(self.kit)
        upper_out = G.stable_id(G._TAG, "port", "uppercase", "text", "out")
        upper_in = G.stable_id(G._TAG, "port", "uppercase", "text", "in")
        for r in fragment["Relationships"]:
            if r["Predicate"] == connector and r["Target"] == upper_out:
                r["Target"] = upper_in

        run = self.validate(fragment)

        self.assertEqual(run.returncode, 1, run.stdout + run.stderr)
        self.assertIn("[Text Studio] the wire from 'Uppercase' to 'Reverse' takes its value from 'uppercase.in.text', "
                      "which is not an out port of 'Uppercase'", run.stdout)

    def test_the_fragment_validator_refuses_a_wire_with_two_input_connectors(self):
        [connector] = self.bound("inputConnector")
        fragment = G.as_fragment(self.kit)
        upper = G.stable_id(G._TAG, "node", "Text Studio", "upper")
        has = next(tid for tid, t in self.kit.things.items() if t["Name"] == "has")
        [wire] = [r["Target"] for r in self.kit.rels.values() if r["Subject"] == upper and r["Predicate"] == has
                  and r["Target"] in self.wires()]
        fragment["Relationships"].append({"Id": "second-input-connector", "Name": "", "Subject": wire, "Predicate": connector,
                                          "Target": G.stable_id(G._TAG, "port", "uppercase", "text", "in"), "Properties": {}})

        run = self.validate(fragment)

        self.assertEqual(run.returncode, 1, run.stdout + run.stderr)
        self.assertIn("[Text Studio] the wire from 'Uppercase' to 'Reverse' relates to 2 ports along inputConnector", run.stdout)

    def test_the_fragment_validator_refuses_a_wire_drawn_as_a_relationship_between_two_nodes(self):
        carries = next(tid for tid, t in self.kit.things.items() if t["Name"] == "carries")
        fragment = G.as_fragment(self.kit)
        upper = G.stable_id(G._TAG, "node", "Text Studio", "upper")
        reverse = G.stable_id(G._TAG, "node", "Text Studio", "rev")
        fragment["Relationships"].append({"Id": "node-to-node-wire", "Name": "", "Subject": upper, "Predicate": carries,
                                          "Target": reverse, "Properties": {}})

        run = self.validate(fragment)

        self.assertEqual(run.returncode, 1, run.stdout + run.stderr)
        self.assertIn("[Text Studio] the wire from 'Uppercase' to 'Reverse' is drawn as a relationship between the two nodes", run.stdout)

    def test_the_fragment_validator_accepts_every_drawing(self):
        run = self.validate(G.as_fragment(self.kit))
        self.assertEqual(run.returncode, 0, run.stdout + run.stderr)
        self.assertIn("Branching Decision", run.stdout)

    def test_the_fragment_validator_refuses_a_choice_no_port_offers(self):
        [offers] = self.bound("choice")
        decide = G.stable_id(G._TAG, "node", "Branching Decision", "decide")
        fragment = G.as_fragment(self.kit)
        fragment["Things"].append({"Id": "unoffered", "Name": "At random", "Properties": {}})
        fragment["Relationships"].append({"Id": "unoffered-choice", "Name": "", "Subject": decide, "Predicate": offers,
                                          "Target": "unoffered", "Properties": {}})

        run = self.validate(fragment)

        self.assertEqual(run.returncode, 1, run.stdout + run.stderr)
        self.assertIn("'Decide' chose 'At random', which no in port of it offers", run.stdout)

    def test_the_fragment_validator_refuses_a_model_whose_pipeline_role_binds_nothing(self):
        fragment = G.as_fragment(self.kit)
        fragment["Relationships"] = [r for r in fragment["Relationships"] if r["Subject"] != G.role_id("pipeline")]

        run = self.validate(fragment)

        self.assertEqual(run.returncode, 1, run.stdout + run.stderr)
        self.assertIn("no Thing is bound to the roles: pipeline", run.stdout)

    @staticmethod
    def validate(fragment):
        import os, subprocess, sys, tempfile
        with tempfile.TemporaryDirectory() as scratch:
            path = os.path.join(scratch, "fragment.seed.json")
            G.dump_seed(fragment, path)
            here = os.path.dirname(os.path.abspath(__file__))
            return subprocess.run([sys.executable, "-I", os.path.join(here, "validate_fragment.py"), path],
                                  capture_output=True, text=True)

    def test_every_drawing_is_published_so_the_model_starts_it(self):
        [pipeline_archetype] = self.bound("pipeline")
        [is_] = [tid for tid, t in self.kit.things.items() if t["Name"] == "is"]
        drawings = [r["Subject"] for r in self.kit.rels.values() if r["Predicate"] == is_ and r["Target"] == pipeline_archetype]
        self.assertTrue(drawings)
        for drawing in drawings:
            self.assertEqual(self.kit.things[drawing]["Properties"]["published"], {"typeInfo": "vos.Boolean", "value": True})

    def test_only_the_services_known_to_write_nothing_rehearse(self):
        rehearsing = {t["Name"] for t in self.kit.things.values() if t["Properties"].get("rehearses", {}).get("value") is True}
        self.assertIn("Echo service", rehearsing)
        self.assertIn("Decide service", rehearsing)
        self.assertNotIn("Water Reserve service", rehearsing)

    def test_the_two_pipelines_drawn_from_outside_stand_for_their_ends(self):
        [stands_for] = self.bound("standsFor")
        stood = {(self.kit.things[r["Subject"]]["Name"], self.kit.things[r["Target"]]["Name"])
                 for r in self.kit.rels.values() if r["Predicate"] == stands_for}
        self.assertEqual(stood, {("A reading batch arrives", "reading batch"), ("The office is told", "Reporting office"),
                                 ("The hour strikes", "Every hour")})

    def test_the_hourly_cadence_is_a_cadence_declaring_its_interval_and_its_last_due_instant(self):
        [cadence_archetype] = self.bound("cadence")
        [is_] = [tid for tid, t in self.kit.things.items() if t["Name"] == "is"]
        [hourly] = [r["Subject"] for r in self.kit.rels.values() if r["Predicate"] == is_ and r["Target"] == cadence_archetype]
        properties = self.kit.things[hourly]["Properties"]
        self.assertEqual(properties["intervalMinutes"], {"typeInfo": "vos.Double", "value": 60.0})
        self.assertEqual(properties["lastDue"]["typeInfo"], "vos.DateTime")
        self.assertIsNone(properties["lastDue"]["value"])


def is_ancestors(things, relationships, tid):
    """Everything above a Thing through `is`, read as the platform reads it."""
    is_ids = {t["Id"] for t in things if t["Name"] == "is"}
    parents = {}
    for r in relationships:
        if r["Predicate"] in is_ids:
            parents.setdefault(r["Subject"], []).append(r["Target"])
    found, pending = set(), [tid]
    while pending:
        for parent in parents.get(pending.pop(), ()):
            if parent not in found:
                found.add(parent)
                pending.append(parent)
    return found


class ModelRootsTests(unittest.TestCase):
    """Configuration is what a model sets up, operation what then happens, and a predicate what a relationship
    runs along; a reader finds each root through its role and asks for everything below it, so every archetype
    must be under exactly one, every predicate under Predicate, and every role Thing under Configuration."""

    def setUp(self):
        G.set_namespace(None)
        self.kit = G.build()
        self.things, self.rels = list(self.kit.things.values()), list(self.kit.rels.values())
        [self.configuration] = bound_by(self.things, self.rels, "configuration")
        [self.operation] = bound_by(self.things, self.rels, "operation")
        [self.predicate] = bound_by(self.things, self.rels, "predicate")
        self.roots = {self.configuration, self.operation, self.predicate}

    def test_every_archetype_is_under_exactly_one_root(self):
        for thing in self.things:
            if not thing.get("IsArchetype") or thing["Id"] in self.roots:
                continue
            above = is_ancestors(self.things, self.rels, thing["Id"])
            self.assertEqual(len(above & self.roots), 1, thing["Name"])

    def test_every_predicate_it_relates_along_or_declares_is_under_the_predicate_root(self):
        for tid in {r["Predicate"] for r in self.rels} | self.kit.predicates:
            self.assertIn(self.predicate, is_ancestors(self.things, self.rels, tid), self.kit.things[tid]["Name"])

    def test_a_predicate_declared_for_the_orchestrator_alone_is_under_the_predicate_root_too(self):
        about = next(t["Id"] for t in self.things if t["Name"] == "about")
        self.assertNotIn(about, {r["Predicate"] for r in self.rels})
        self.assertIn(self.predicate, is_ancestors(self.things, self.rels, about))

    def test_no_predicate_is_placed_directly_under_configuration(self):
        placed = {r["Subject"] for r in self.rels if r["Predicate"] == self.kit_is() and r["Target"] == self.configuration}
        self.assertEqual(sorted(self.kit.things[tid]["Name"] for tid in placed & self.kit.predicates), [])

    def kit_is(self):
        return next(t["Id"] for t in self.things if t["Name"] == "is")

    def test_runs_are_operation_and_the_rest_is_configuration(self):
        by_name = {t["Name"]: t["Id"] for t in self.things}
        for operational in ("PipelineRun", "NodeRun", "SentMessage"):
            self.assertIn(self.operation, is_ancestors(self.things, self.rels, by_name[operational]), operational)
        for configured in ("Pipeline", "Service", "PlatformServiceConnection", "Strategy"):
            self.assertIn(self.configuration, is_ancestors(self.things, self.rels, by_name[configured]), configured)

    def test_every_role_thing_is_under_configuration(self):
        for tid in self.kit.role_things:
            self.assertIn(self.configuration, is_ancestors(self.things, self.rels, tid), self.kit.things[tid]["Name"])

    def test_merge_places_onto_the_roots_the_target_binds_and_adds_no_second_root(self):
        seed = {"Name": "Target",
                "Things": [{"Id": "target-binds", "Name": "binds", "Properties": {}},
                           {"Id": G.role_id("configuration"), "Name": "configuration role", "Properties": {}},
                           {"Id": G.role_id("operation"), "Name": "operation role", "Properties": {}},
                           {"Id": G.role_id("predicate"), "Name": "predicate role", "Properties": {}},
                           {"Id": "target-set-up", "Name": "Set-up", "IsArchetype": True, "Properties": {}},
                           {"Id": "target-doing", "Name": "Doing", "IsArchetype": True, "Properties": {}},
                           {"Id": "target-joining", "Name": "Joining", "IsArchetype": True, "Properties": {}}],
                "Relationships": [{"Id": "b1", "Name": "", "Subject": G.role_id("configuration"), "Predicate": "target-binds", "Target": "target-set-up", "Properties": {}},
                                  {"Id": "b2", "Name": "", "Subject": G.role_id("operation"), "Predicate": "target-binds", "Target": "target-doing", "Properties": {}},
                                  {"Id": "b3", "Name": "", "Subject": G.role_id("predicate"), "Predicate": "target-binds", "Target": "target-joining", "Properties": {}}]}
        G.merge_into(G.build(), seed)

        self.assertEqual([t["Name"] for t in seed["Things"] if t["Name"] in ("Configuration", "Operation", "Predicate")], [])
        [pipeline] = bound_in_seed(seed, "pipeline")
        [run] = bound_in_seed(seed, "pipelineRun")
        about = next(t["Id"] for t in seed["Things"] if t["Name"] == "about")
        self.assertIn("target-set-up", is_ancestors(seed["Things"], seed["Relationships"], pipeline))
        self.assertIn("target-doing", is_ancestors(seed["Things"], seed["Relationships"], run))
        self.assertIn("target-joining", is_ancestors(seed["Things"], seed["Relationships"], about))

    def test_remove_leaves_the_vocabulary_where_the_roots_hold_it(self):
        seed = {"Name": "Target", "Things": [], "Relationships": []}
        G.merge_into(G.build(), seed)
        G.remove_from(G.build(), seed)

        [configuration] = bound_in_seed(seed, "configuration")
        [pipeline] = bound_in_seed(seed, "pipeline")
        self.assertIn(configuration, is_ancestors(seed["Things"], seed["Relationships"], pipeline))
