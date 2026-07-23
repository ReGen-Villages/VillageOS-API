"""Spec for the playground generator's id derivation. --namespace makes the same DAGs disjoint per model
so two seeds can coexist in one broker; the default (no namespace) keeps the shared standalone identity;
and merge still reconciles shared vocabulary (is/has/archetypes) against a target by name, not by id.
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


if __name__ == "__main__":
    unittest.main()
