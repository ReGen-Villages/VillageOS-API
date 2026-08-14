"""Behavioural specification for the containment-layout importer: names build the tree, a name that
does not follow the standard is reported rather than guessed at, an explicit row overrides one field
and leaves the rest, the fragment carries every node once, and a re-run is an upsert rather than a
second structure.

Domain-agnostic, like the tool: the structures here are sites, wings, rooms and cells.
"""
import contextlib
import io
import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import layout as L


def spec(**overrides):
    """A three-level structure whose names carry it: S-01-01 is cell 01 of room 01 of site S."""
    base = {
        "name": "sample",
        "containsPredicate": "holds",
        "levels": [
            {"name": "site", "type": "Site"},
            {"name": "room", "type": "Room", "properties": {"height_m": 6}},
            {"name": "cell", "type": "Cell", "properties": {"capacity_units": 100}},
        ],
        "pattern": r"(?P<site>[A-Z]+)-(?P<room>\d{2})-(?P<cell>\d{2})",
        "leaves": ["S-01-01", "S-01-02", "S-02-01"],
    }
    base.update(overrides)
    return base


KNOWN_IDS = {
    "holds": "id-holds",
    "is": "id-is",
    "Site": "id-Site",
    "Room": "id-Room",
    "Cell": "id-Cell",
    "ColdCell": "id-ColdCell",
}


class FakeMycelium:
    """Records the fragments it is given and answers name lookups from a fixed model, so a re-run
    can be watched without a broker."""

    def __init__(self, known=None):
        self.known = dict(known or KNOWN_IDS)
        self.fragments = []

    def thing_id_by_name(self, name):
        return self.known.get(name)

    def apply_fragment(self, things, relationships, name="fragment"):
        self.fragments.append({"Name": name, "Things": things, "Relationships": relationships})
        return {}


class ReadsTheStructure(unittest.TestCase):
    def test_names_build_every_intermediate_node_once(self):
        built = L.read_layout(spec())

        by_name = {node.name: node for node in built.nodes}
        self.assertEqual(
            sorted(by_name), ["S", "S-01", "S-01-01", "S-01-02", "S-02", "S-02-01"],
            "the two rooms and the site come from the leaf names, each built once",
        )
        self.assertEqual(by_name["S-01-01"].parent.name, "S-01")
        self.assertEqual(by_name["S-01"].parent.name, "S")
        self.assertIsNone(by_name["S"].parent)

    def test_levels_are_read_from_the_pattern_groups(self):
        built = L.read_layout(spec())

        levels = {node.name: node.level.name for node in built.nodes}
        self.assertEqual(levels["S"], "site")
        self.assertEqual(levels["S-01"], "room")
        self.assertEqual(levels["S-01-01"], "cell")

    def test_a_name_that_does_not_match_is_reported_with_its_line(self):
        with self.assertRaises(L.LayoutError) as raised:
            L.read_layout(spec(leaves=["S-01-01", "not-a-name"]))

        self.assertIn("line 2", str(raised.exception))
        self.assertIn("not-a-name", str(raised.exception))

    def test_a_pattern_missing_a_level_is_refused(self):
        with self.assertRaises(L.LayoutError) as raised:
            L.read_layout(spec(pattern=r"(?P<site>[A-Z]+)-(?P<room>\d{2})-\d{2}"))

        self.assertIn("cell", str(raised.exception))

    def test_level_defaults_reach_every_node_of_that_level(self):
        built = L.read_layout(spec())

        cells = [node for node in built.nodes if node.level.name == "cell"]
        self.assertTrue(cells)
        for cell in cells:
            self.assertEqual(cell.properties["capacity_units"], 100)

    def test_a_row_overrides_one_field_and_leaves_the_rest_derived(self):
        built = L.read_layout(spec(nodes=[
            {"name": "S-01-01", "properties": {"capacity_units": 250}, "types": ["ColdCell"]},
        ]))

        by_name = {node.name: node for node in built.nodes}
        self.assertEqual(by_name["S-01-01"].properties["capacity_units"], 250)
        self.assertEqual(by_name["S-01-02"].properties["capacity_units"], 100,
                         "a row speaks for its own node only")
        self.assertEqual(by_name["S-01-01"].type_names, ["Cell", "ColdCell"])
        self.assertEqual(by_name["S-01-01"].parent.name, "S-01",
                         "an override does not move a node")

    def test_a_row_can_add_a_node_the_names_do_not_reach(self):
        built = L.read_layout(spec(nodes=[
            {"name": "S-99-odd", "level": "cell", "within": "S-01",
             "properties": {"capacity_units": 7}},
        ]))

        by_name = {node.name: node for node in built.nodes}
        self.assertEqual(by_name["S-99-odd"].parent.name, "S-01")
        self.assertEqual(by_name["S-99-odd"].properties["capacity_units"], 7)

    def test_a_row_with_nowhere_to_go_is_refused(self):
        with self.assertRaises(L.LayoutError) as raised:
            L.read_layout(spec(nodes=[{"name": "S-99-odd"}]))

        self.assertIn("S-99-odd", str(raised.exception))

    def test_no_levels_is_refused(self):
        with self.assertRaises(L.LayoutError) as raised:
            L.read_layout(spec(levels=[]))

        self.assertIn("levels", str(raised.exception))

    # A pattern whose groups end at the same place gives two levels the same node name, and a name
    # can only be one thing. Better said than quietly resolved one way or the other.
    def test_one_name_read_as_two_levels_is_refused(self):
        with self.assertRaises(L.LayoutError) as raised:
            L.read_layout(spec(pattern=r"(?P<site>[A-Z]+)(?P<room>)-(?P<cell>\d{2})",
                               leaves=["S-01"]))

        self.assertIn("site", str(raised.exception))
        self.assertIn("room", str(raised.exception))

    def test_a_row_placed_within_something_absent_is_refused(self):
        with self.assertRaises(L.LayoutError) as raised:
            L.read_layout(spec(nodes=[
                {"name": "S-99-odd", "level": "cell", "within": "nowhere"},
            ]))

        self.assertIn("nowhere", str(raised.exception))

    def test_a_level_missing_a_key_is_named_so_the_file_can_be_fixed(self):
        levels = [{"name": "site", "type": "Site"}, {"name": "room"}]

        with self.assertRaises(L.LayoutError) as raised:
            L.read_layout(spec(levels=levels))

        self.assertIn("'type'", str(raised.exception))
        self.assertIn("room", str(raised.exception))

    def test_reading_the_same_file_twice_gives_the_same_tree_in_the_same_order(self):
        first = [node.name for node in L.read_layout(spec()).nodes]
        second = [node.name for node in L.read_layout(spec()).nodes]

        self.assertEqual(first, second)


class BuildsTheFragment(unittest.TestCase):
    def setUp(self):
        self.layout = L.read_layout(spec())
        self.things, self.relationships = L.build_fragment(self.layout, KNOWN_IDS)

    def test_every_node_is_one_thing_carrying_its_properties(self):
        self.assertEqual(len(self.things), len(self.layout.nodes))
        ids = [thing["Id"] for thing in self.things]
        self.assertEqual(len(ids), len(set(ids)), "no node is built twice")

        cell = next(t for t in self.things if t["Name"] == "S-01-01")
        self.assertIn("capacity_units", cell["Properties"])

    def test_each_node_is_held_by_its_parent_and_the_root_by_nothing(self):
        holds = [rel for rel in self.relationships if rel["Predicate"] == "id-holds"]

        self.assertEqual(len(holds), len(self.layout.nodes) - 1,
                         "every node but the root is held by exactly one other")
        root_id = next(node.id for node in self.layout.nodes if node.parent is None)
        self.assertNotIn(root_id, [rel["Target"] for rel in holds])

    def test_each_node_is_of_its_levels_type(self):
        is_edges = {(rel["Subject"], rel["Target"])
                    for rel in self.relationships if rel["Predicate"] == "id-is"}
        by_name = {node.name: node for node in self.layout.nodes}

        self.assertIn((by_name["S"].id, "id-Site"), is_edges)
        self.assertIn((by_name["S-01"].id, "id-Room"), is_edges)
        self.assertIn((by_name["S-01-01"].id, "id-Cell"), is_edges)

    def test_an_extra_type_is_a_second_is_edge(self):
        built = L.read_layout(spec(nodes=[{"name": "S-01-01", "types": ["ColdCell"]}]))
        _, relationships = L.build_fragment(built, KNOWN_IDS)

        node = next(one for one in built.nodes if one.name == "S-01-01")
        targets = {rel["Target"] for rel in relationships
                   if rel["Subject"] == node.id and rel["Predicate"] == "id-is"}
        self.assertEqual(targets, {"id-Cell", "id-ColdCell"})

    def test_a_name_with_no_id_is_named_rather_than_guessed_at(self):
        with self.assertRaises(L.LayoutError) as raised:
            L.build_fragment(self.layout, {"holds": "id-holds", "is": "id-is"})

        self.assertIn("Cell", str(raised.exception))

    def test_ids_are_stable_across_reads(self):
        again, _ = L.build_fragment(L.read_layout(spec()), KNOWN_IDS)

        self.assertEqual([thing["Id"] for thing in self.things],
                         [thing["Id"] for thing in again])

    def test_a_rerun_that_extends_the_file_keeps_the_ids_it_already_used(self):
        extended = L.read_layout(spec(leaves=["S-01-01", "S-01-02", "S-02-01", "S-02-02"]))
        extended_things, _ = L.build_fragment(extended, KNOWN_IDS)

        before = {thing["Id"] for thing in self.things}
        after = {thing["Id"] for thing in extended_things}
        self.assertTrue(before < after, "the first run's Things are the same Things in the second")
        self.assertEqual(len(after - before), 1, "only the new node is new")


class RunsTheImporter(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.mkdtemp()
        self.spec_path = os.path.join(self.directory, "layout.json")
        with open(self.spec_path, "w", encoding="utf-8") as handle:
            json.dump(spec(), handle)
        self.ids_path = os.path.join(self.directory, "ids.json")
        with open(self.ids_path, "w", encoding="utf-8") as handle:
            json.dump(KNOWN_IDS, handle)

    def test_an_api_key_argument_is_refused(self):
        """The key is read from the environment by the shared client. A command line is readable by
        every process on the host, so a run that puts one there is stopped rather than sent."""
        stderr = io.StringIO()

        with contextlib.redirect_stderr(stderr), self.assertRaises(SystemExit):
            L.main([self.spec_path, "--api-key", "KEY-123"], client=FakeMycelium())

        self.assertIn("unrecognized arguments: --api-key", stderr.getvalue())

    def test_a_dry_run_writes_the_fragment_that_would_have_been_sent(self):
        out = os.path.join(self.directory, "fragment.json")

        L.main([self.spec_path, "--ids", self.ids_path, "--out", out])

        with open(out, encoding="utf-8") as handle:
            written = json.load(handle)
        things, relationships = L.build_fragment(L.read_layout(spec()), KNOWN_IDS)
        self.assertEqual(written["Things"], things)
        self.assertEqual(written["Relationships"], relationships)
        self.assertEqual(written["Name"], "sample")

    def test_resolving_asks_the_model_for_every_name_the_spec_uses(self):
        fake = FakeMycelium()

        resolved = L.resolve_ids(fake, L.names_used(L.read_layout(spec())))

        self.assertEqual(resolved["holds"], "id-holds")
        self.assertEqual(resolved["Cell"], "id-Cell")
        self.assertEqual(resolved["is"], "id-is")

    def test_applying_sends_one_fragment_carrying_the_whole_structure(self):
        fake = FakeMycelium()

        L.main([self.spec_path], client=fake)

        self.assertEqual(len(fake.fragments), 1, "a structure goes in as one fragment, not per node")
        sent = fake.fragments[0]
        things, relationships = L.build_fragment(L.read_layout(spec()), KNOWN_IDS)
        self.assertEqual(sent["Things"], things)
        self.assertEqual(sent["Relationships"], relationships)
        self.assertEqual(sent["Name"], "sample")

    def test_applying_twice_sends_the_same_fragment_both_times(self):
        fake = FakeMycelium()

        L.main([self.spec_path], client=fake)
        L.main([self.spec_path], client=fake)

        self.assertEqual(fake.fragments[0], fake.fragments[1],
                         "the second run is the same upsert, which is what leaves a model alone")

    def test_an_id_given_in_the_file_wins_over_the_one_the_model_holds(self):
        fake = FakeMycelium(known={**KNOWN_IDS, "Cell": "id-the-model-holds"})
        pinned = os.path.join(self.directory, "pinned.json")
        with open(pinned, "w", encoding="utf-8") as handle:
            json.dump({"Cell": "id-the-file-pins"}, handle)

        L.main([self.spec_path, "--ids", pinned], client=fake)

        targets = {rel["Target"] for rel in fake.fragments[0]["Relationships"]}
        self.assertIn("id-the-file-pins", targets)
        self.assertNotIn("id-the-model-holds", targets)

    def test_a_spec_that_cannot_be_read_fails_before_the_model_is_asked_anything(self):
        broken = os.path.join(self.directory, "broken.json")
        with open(broken, "w", encoding="utf-8") as handle:
            json.dump(spec(leaves=["not-a-name"]), handle)
        fake = FakeMycelium()

        with self.assertRaises(L.LayoutError):
            L.main([broken], client=fake)

        self.assertEqual(fake.fragments, [])

    def test_a_name_the_model_does_not_hold_is_absent_rather_than_invented(self):
        fake = FakeMycelium(known={"holds": "id-holds"})

        resolved = L.resolve_ids(fake, L.names_used(L.read_layout(spec())))

        self.assertEqual(resolved, {"holds": "id-holds"})


if __name__ == "__main__":
    unittest.main()
