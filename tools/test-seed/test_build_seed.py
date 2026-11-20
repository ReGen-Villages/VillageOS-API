"""What the test seed has to hold for the tests that read it, and that the committed files are the generator's
own output."""
import json
import re
import tempfile
import unittest
import uuid
from pathlib import Path

import build_seed as B
from generate import ROLE_ARCHETYPE, ROLE_NAMESPACE, dump_seed, role_id

# Words the land-intake and site-analysis templates use for their own kinds, predicates and pages. A seed
# holding one would no longer show that a tool works on a model that has nothing to do with them.
WORDS_OF_THE_LAND_TEMPLATES = (
    "submission", "study", "studies", "parcel", "programme", "allocation", "hazard", "disposition", "proposes",
    "resolvedas", "coverage", "contact", "boundary", "promot",
)

BINDING_KINDS_THE_READER_RESOLVES = {
    "const", "stateCount", "stateList", "thingList", "aggregate", "property", "related", "stateOf", "verdict",
    "origin", "working", "ratio", "compareEntities", "timeseries", "latest", "service", "history",
}


def written(seed):
    with tempfile.TemporaryDirectory() as folder:
        path = Path(folder) / "seed.json"
        dump_seed(seed, path)
        return path.read_text(encoding="utf-8")


class Seed:
    def __init__(self, seed):
        self.things = seed["Things"]
        self.relationships = seed["Relationships"]
        self.by_id = {thing["Id"]: thing for thing in self.things}
        self.named = {}
        for thing in self.things:
            self.named.setdefault(thing["Name"], []).append(thing)

    def one(self, name):
        (thing,) = [each for each in self.named[name] if each["Id"] != role_id(name[0].lower() + name[1:])]
        return thing

    def reached(self, subject_id, predicate_name):
        predicates = {thing["Id"] for thing in self.named.get(predicate_name, [])}
        return [self.by_id[each["Target"]] for each in self.relationships
                if each["Subject"] == subject_id and each["Predicate"] in predicates]

    def reaching(self, target_id, predicate_name):
        predicates = {thing["Id"] for thing in self.named.get(predicate_name, [])}
        return [self.by_id[each["Subject"]] for each in self.relationships
                if each["Target"] == target_id and each["Predicate"] in predicates]

    def bound_by(self, word):
        return self.reached(role_id(word), "binds")

    def members_of(self, kind_name):
        return self.reaching(self.one(kind_name)["Id"], "is")

    def pages(self):
        (page_kind,) = self.bound_by("page")
        return self.reaching(page_kind["Id"], "is")


def bindings_in(node):
    if isinstance(node, dict):
        if isinstance(node.get("kind"), str) and node["kind"] in BINDING_KINDS_THE_READER_RESOLVES:
            yield node
        for value in node.values():
            yield from bindings_in(value)
    elif isinstance(node, list):
        for value in node:
            yield from bindings_in(value)


def widgets_in(description):
    return [widget for section in description["sections"] for widget in section["widgets"]]


class CommittedFilesTests(unittest.TestCase):
    def test_each_committed_seed_is_what_the_generator_writes(self):
        for path, seed in B.committed_seeds().items():
            with self.subTest(path.name):
                self.assertEqual(path.read_text(encoding="utf-8"), written(seed),
                                 f"run tools/test-seed/build_seed.py and commit {path.name}")

    def test_the_seed_the_validator_must_refuse_names_its_one_refusal_beside_it(self):
        refusals = B.COMMITTED_SEED_WITH_AN_UNREADABLE_PAGE.with_name(
            B.COMMITTED_SEED_WITH_AN_UNREADABLE_PAGE.name.replace(".json", ".refusals.txt"))
        self.assertEqual([f"Dashboard '{B.UNREADABLE_PAGE}': its 'specification' is not valid JSON"],
                         refusals.read_text(encoding="utf-8").splitlines())
        self.assertFalse(B.COMMITTED_SEED.with_name(B.COMMITTED_SEED.name.replace(".json", ".refusals.txt")).exists())

    def test_building_twice_writes_the_same_seed(self):
        self.assertEqual(written(B.as_seed(B.build())), written(B.as_seed(B.build())))

    def test_the_playground_generator_is_read_and_not_changed_by_being_imported(self):
        before = B.playground.as_fragment(B.playground.build())
        B.build()
        self.assertEqual(before, B.playground.as_fragment(B.playground.build()))


class VocabularyTests(unittest.TestCase):
    def test_the_generator_and_both_seeds_hold_no_word_of_the_land_templates(self):
        texts = {path.name: path.read_text(encoding="utf-8") for path in B.committed_seeds()}
        texts["build_seed.py"] = Path(B.__file__).read_text(encoding="utf-8")
        for name, text in texts.items():
            for word in WORDS_OF_THE_LAND_TEMPLATES:
                with self.subTest(file=name, word=word):
                    self.assertNotIn(word, text.lower())


class IdentifierTests(unittest.TestCase):
    def setUp(self):
        self.seed = B.as_seed(B.build())

    def test_every_identifier_is_a_version_5_identifier(self):
        for entity in (*self.seed["Things"], *self.seed["Relationships"]):
            self.assertEqual(5, uuid.UUID(entity["Id"]).version, entity["Name"])

    def test_every_role_thing_has_the_identifier_of_its_word_and_binds_one_thing(self):
        seed = Seed(self.seed)
        role_kind = seed.by_id[ROLE_ARCHETYPE]
        roles = seed.reaching(role_kind["Id"], "is")
        self.assertTrue(roles)
        for role in roles:
            word = role["Name"][0].lower() + role["Name"][1:]
            self.assertEqual(str(uuid.uuid5(ROLE_NAMESPACE, word)), role["Id"])
            self.assertEqual(1, len(seed.reached(role["Id"], "binds")), role["Name"])

    def test_no_identifier_is_used_twice(self):
        identifiers = [entity["Id"] for entity in (*self.seed["Things"], *self.seed["Relationships"])]
        self.assertEqual(len(identifiers), len(set(identifiers)))


class WhatTheSeedHoldsTests(unittest.TestCase):
    def setUp(self):
        self.seed = Seed(B.as_seed(B.build()))
        (page,) = self.seed.pages()
        self.description = json.loads(page["Properties"]["specification"]["value"])

    def test_each_part_a_tool_reads_through_a_role_is_bound(self):
        for word in ("page", "displaySettings", "propertyName", "connection", "service", "trigger", "httpTrigger",
                     "graphTrigger", "stateTrigger", "stateWatch", "range", "judgedThing", "callRecord",
                     "notifiedConnection", "completesWhen", "callCompletion", "whenTheServiceAnswers",
                     "configuration", "operation", "predicate"):
            with self.subTest(word):
                self.assertEqual(1, len(self.seed.bound_by(word)))

    def test_a_connection_started_by_a_state_watches_a_range_and_has_a_service(self):
        (state,) = self.seed.bound_by("stateTrigger")
        (connection,) = self.seed.reaching(state["Id"], "triggeredBy")
        (watched,) = self.seed.reached(connection["Id"], "watches")
        (range_kind,) = self.seed.bound_by("range")
        self.assertIn(range_kind, self.seed.reached(watched["Id"], "is"))
        (service,) = self.seed.reached(connection["Id"], "has")
        (prototype,) = self.seed.reached(service["Id"], "is")
        (service_kind,) = self.seed.bound_by("service")
        self.assertIn(service_kind, self.seed.reached(prototype["Id"], "is"))

    def test_the_compared_kind_has_three_members_and_two_are_named_outside_ascii(self):
        members = self.seed.members_of(self.description["compare"]["archetype"])
        self.assertGreaterEqual(len(members), 3)
        self.assertGreaterEqual(len([each for each in members if not each["Name"].isascii()]), 2)

    def test_the_page_holds_a_binding_of_every_kind_the_reader_resolves(self):
        held = {binding["kind"] for binding in bindings_in(self.description)}
        self.assertEqual(BINDING_KINDS_THE_READER_RESOLVES, held)

    def test_the_action_list_draws_its_choices_from_the_model_stamps_and_lists_three_of_its_own(self):
        (action,) = [widget for widget in widgets_in(self.description) if widget["type"] == "action"]
        writes = action["writes"]
        drawn = {each["Name"] for each in self.seed.members_of(writes["choicesFrom"]["from"]["archetype"])}
        self.assertTrue(self.seed.named[writes["choicesFrom"]["predicate"]])
        self.assertTrue(writes["stamps"])
        replacing = [choice for choice in writes["choices"] if choice.get("target") in drawn]
        to_an_endpoint = [choice for choice in writes["choices"] if choice.get("via") == B.ECHO_SUBDOMAIN]
        to_the_platform = [choice for choice in writes["choices"] if choice.get("via", "").startswith("/")]
        self.assertEqual((1, 1, 1), (len(replacing), len(to_an_endpoint), len(to_the_platform)))
        self.assertTrue(any(asked.get("options") for asked in to_an_endpoint[0]["asks"]))

    def test_the_service_binding_reads_the_declared_endpoint_by_its_path(self):
        (service,) = [binding for binding in bindings_in(self.description) if binding["kind"] == "service"]
        self.assertEqual(f"/api/endpoints/{B.ECHO_SUBDOMAIN}", service["endpoint"])

    def test_the_form_posts_to_the_endpoint_the_model_declares(self):
        (form,) = [widget for widget in widgets_in(self.description) if widget["type"] == "form"]
        (http,) = self.seed.bound_by("httpTrigger")
        subdomains = {connection["Properties"]["Subdomain"]["value"]
                      for connection in self.seed.reaching(http["Id"], "triggeredBy")}
        self.assertIn(form["writes"]["via"], subdomains)

    def test_one_catchment_holds_a_property_of_each_origin(self):
        kind = self.seed.one("Catchment")
        catchment = self.seed.one("Ålvik north slope")
        stated = catchment["InheritedOverrides"][kind["Id"]]["Properties"]
        declared = kind["Properties"]
        self.assertEqual("FactOnly", declared["areaHectares"].get("writeKind"))
        self.assertIn("areaHectares", stated)
        self.assertEqual("ObservationOnly", declared["rainfallMillimetresPerYear"].get("writeKind"))
        self.assertIn("value", declared["runoffCoefficient"])
        self.assertNotIn("runoffCoefficient", stated)
        self.assertNotIn("writeKind", declared["siltDepthMetres"])
        self.assertIn("siltDepthMetres", stated)

    def test_the_derived_figures_cover_the_cases_the_walk_to_inputs_has_to_tell_apart(self):
        derived = self.seed.one("Catchment")["DerivedProperties"]
        terms = lambda name: set(re.findall(r"[A-Za-z][A-Za-z0-9]*", derived[name]["Expression"]))
        self.assertIn("harvestM3PerYear", terms("supplyM3PerYear"), "a figure worked out from another figure")
        self.assertEqual(2, derived["surfaceBalanceM3PerYear"]["Expression"].count("areaHectares"),
                         "one input added and taken away in one formula")
        self.assertEqual({"supplyM3PerYear", "irrigationM3PerYear"}, terms("balanceM3PerYear"),
                         "one input reached along a route that adds and a route that takes away")
        self.assertIn("areaHectares", terms("harvestM3PerYear") & terms("irrigationM3PerYear"))
        self.assertEqual("Reservoir", derived["storedM3"]["RelatedType"], "a total over members")

    def test_an_input_with_bounds_declares_a_default_inside_them(self):
        kind = self.seed.one("Catchment")
        bounded = [name for name in self.seed.members_of("PropertyName") if "lowest" in name["Properties"]]
        self.assertTrue(bounded)
        for name in bounded:
            lowest, highest, step = (name["Properties"][each]["value"] for each in ("lowest", "highest", "stepSize"))
            self.assertLess(lowest, highest)
            self.assertTrue(0 < step <= highest - lowest)
            default = kind["Properties"][name["Properties"]["namedProperty"]["value"]].get("value")
            if default is not None:
                self.assertTrue(lowest <= default <= highest, name["Name"])

    def test_only_the_second_seed_holds_a_page_whose_description_cannot_be_read(self):
        with_one = Seed(B.as_seed(B.build(with_an_unreadable_page=True)))
        unreadable = []
        for page in with_one.pages():
            try:
                json.loads(page["Properties"]["specification"]["value"])
            except json.JSONDecodeError:
                unreadable.append(page["Name"])
        self.assertEqual(1, len(unreadable))
        self.assertEqual(1, len(self.seed.pages()))


if __name__ == "__main__":
    unittest.main()
