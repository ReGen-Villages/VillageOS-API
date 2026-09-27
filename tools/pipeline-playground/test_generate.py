"""Spec for the playground generator's id derivation and for the marks its archetypes carry. --namespace
makes the same DAGs disjoint per model so two seeds can coexist in one broker; the default (no namespace)
keeps the shared standalone identity; merge reconciles the built-in predicates against a target by name and
an archetype by the role flag it carries, so a target that renamed one keeps its own.
"""
import unittest

import generate as G


def build_with_namespace(name):
    G.set_namespace(name)
    try:
        return G.build()
    finally:
        G.set_namespace(None)  # never leak the namespace into another test


class NamespaceTests(unittest.TestCase):
    def test_two_namespaces_share_no_thing_ids(self):
        alpha = build_with_namespace("Alpha")
        beta = build_with_namespace("Beta")
        self.assertTrue(alpha.things and beta.things)
        self.assertEqual(set(alpha.things) & set(beta.things), set(),
                         "namespaced builds must not share any Thing id")

    def test_same_namespace_is_stable(self):
        first = build_with_namespace("Alpha")
        second = build_with_namespace("Alpha")
        self.assertEqual(set(first.things), set(second.things))
        self.assertEqual(set(first.rels), set(second.rels))

    def test_namespaced_ids_differ_from_the_default_root(self):
        G.set_namespace(None)
        default = G.build()
        namespaced = build_with_namespace("Alpha")
        self.assertEqual(set(default.things) & set(namespaced.things), set())

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


class RoleFlagTests(unittest.TestCase):
    """What the editor and the orchestrator read: a model says what an archetype is for by marking it, and
    a merge finds the target's archetype by that mark rather than by the name this generator happens to use."""

    def test_every_archetype_carries_the_flag_for_its_role(self):
        kit = G.build()
        self.assertEqual(set(kit.role_flag.values()), set(G.ROLE_FLAG.values()) | set(G.PREDICATE_FLAG.values()))
        for tid, flag in kit.role_flag.items():
            self.assertEqual(kit.things[tid]["Properties"][flag], {"typeInfo": "vos.Boolean", "value": True})

    def test_merge_reuses_a_renamed_archetype_the_target_has_marked(self):
        seed = {"Name": "Target",
                "Things": [{"Id": "target-pipeline", "Name": "Workflow",
                            "Properties": {"__IsPipelineArchetype": {"typeInfo": "vos.Boolean", "value": True}}}],
                "Relationships": []}
        G.merge_into(G.build(), seed)

        carriers = [t for t in seed["Things"] if "__IsPipelineArchetype" in t["Properties"]]
        self.assertEqual([(t["Id"], t["Name"]) for t in carriers], [("target-pipeline", "Workflow")])
        self.assertTrue(any(r["Target"] == "target-pipeline" for r in seed["Relationships"]),
                        "the playground's pipelines must `is` the target's own archetype")

    def test_merging_twice_leaves_one_carrier_per_role_and_adds_nothing(self):
        seed = {"Name": "Target", "Things": [], "Relationships": []}
        G.merge_into(G.build(), seed)
        after_first = (len(seed["Things"]), len(seed["Relationships"]))
        G.merge_into(G.build(), seed)

        self.assertEqual((len(seed["Things"]), len(seed["Relationships"])), after_first)
        for flag in G.ROLE_FLAG.values():
            carriers = [t for t in seed["Things"] if flag in t["Properties"]]
            self.assertEqual(len(carriers), 1, f"{flag} must be carried by exactly one archetype")

    def test_merge_marks_an_archetype_the_target_left_unmarked(self):
        seed = {"Name": "Target",
                "Things": [{"Id": "target-pipeline", "Name": "Pipeline", "Properties": {}}],
                "Relationships": []}
        G.merge_into(G.build(), seed)

        carriers = [t for t in seed["Things"] if "__IsPipelineArchetype" in t["Properties"]]
        self.assertEqual([t["Id"] for t in carriers], ["target-pipeline"])
        self.assertIs(carriers[0]["Properties"]["__IsPipelineArchetype"]["value"], True)


if __name__ == "__main__":
    unittest.main()


class CatalystVocabularyTests(unittest.TestCase):
    """The rails of the Pipeline page read external systems, kinds of message and what a boundary node
    stands for off marks, so the kit declares each once and relates through marked predicates."""

    def setUp(self):
        G.set_namespace(None)
        self.kit = G.build()

    def carriers(self, flag):
        return [tid for tid, t in self.kit.things.items()
                if G._prop(t.get("Properties"), flag) is True]

    def test_each_new_role_is_marked_on_one_thing(self):
        for flag in ("__IsExternalSystemArchetype", "__IsMessageKindArchetype", "__IsTriggerPredicate", "__IsSendsPredicate",
                     "__IsToldPredicate", "__IsArrivesAtPredicate", "__IsStandsForPredicate", "__IsRunSubjectPredicate"):
            self.assertEqual(len(self.carriers(flag)), 1, flag)

    def test_a_marked_predicate_reconciles_onto_a_target_that_holds_it_by_name(self):
        seed = {"Name": "Target",
                "Things": [{"Id": "target-stands-for", "Name": "standsFor", "Properties": {}}],
                "Relationships": []}
        G.merge_into(self.kit, seed)
        stands_for = [t for t in seed["Things"] if t["Name"] == "standsFor"]
        self.assertEqual(len(stands_for), 1)
        self.assertEqual(stands_for[0]["Id"], "target-stands-for")
        self.assertIs(G._prop(stands_for[0]["Properties"], "__IsStandsForPredicate"), True)

    def test_every_sent_kind_arrives_at_a_door_and_every_told_kind_is_a_kind(self):
        [sends] = self.carriers("__IsSendsPredicate")
        [told] = self.carriers("__IsToldPredicate")
        [arrives_at] = self.carriers("__IsArrivesAtPredicate")
        [kind_archetype] = self.carriers("__IsMessageKindArchetype")
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

    def test_every_connection_is_reached_over_http(self):
        [triggered_by] = self.carriers("__IsTriggerPredicate")
        [connection_archetype] = self.carriers("__IsConnectionArchetype")
        [is_] = [tid for tid, t in self.kit.things.items() if t["Name"] == "is"]
        rels = list(self.kit.rels.values())
        connections = {r["Subject"] for r in rels if r["Predicate"] == is_ and r["Target"] == connection_archetype}
        for connection in connections:
            triggers = [self.kit.things[r["Target"]]["Name"] for r in rels if r["Subject"] == connection and r["Predicate"] == triggered_by]
            self.assertEqual(triggers, ["http"], self.kit.things[connection]["Name"])

    def test_the_two_pipelines_drawn_from_outside_stand_for_their_ends(self):
        [stands_for] = self.carriers("__IsStandsForPredicate")
        stood = {(self.kit.things[r["Subject"]]["Name"], self.kit.things[r["Target"]]["Name"])
                 for r in self.kit.rels.values() if r["Predicate"] == stands_for}
        self.assertEqual(stood, {("A reading batch arrives", "reading batch"), ("The office is told", "Reporting office")})
