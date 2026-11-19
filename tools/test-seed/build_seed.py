#!/usr/bin/env python3
"""Build the seed the tests of the MCP server and of the pages library read.

The seed is a small model of sites, catchments, reservoirs and springs. It declares what those tests need a
model to hold: derived figures and the ranges that judge them, words for the inputs, a connection a state
starts, and pages with every kind of binding, an action list and a form.

    python3 build_seed.py                 # write both seeds into Tests/vos.Tests.Shared/Fixtures
    python3 build_seed.py --out DIRECTORY # write them somewhere else

The committed seeds are this script's output; test_build_seed.py fails when they differ.
"""

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "pipeline-playground"))
import generate as playground
from generate import ROLE_ARCHETYPE, Kit, typed

TAG = "test-seed"
SEED_NAME = "Catchments"
REPOSITORY_ROOT = Path(__file__).resolve().parent.parent.parent
SEED_FOLDER = REPOSITORY_ROOT / "Tests" / "vos.Tests.Shared" / "Fixtures"
COMMITTED_SEED = SEED_FOLDER / f"{SEED_NAME}.seed.json"
COMMITTED_SEED_WITH_AN_UNREADABLE_PAGE = SEED_FOLDER / f"{SEED_NAME}WithAnUnreadablePage.seed.json"

ECHO_SUBDOMAIN = "echo"
ECHO_PORT = 5790
TARGET_DAYS_OF_SUPPLY = 14

LANGUAGES_WORDED = ("en", "de", "ar")


def identifier(*parts):
    return playground.stable_id(TAG, *parts)


def wording(english, german, arabic):
    return typed(json.dumps(dict(zip(LANGUAGES_WORDED, (english, german, arabic))), ensure_ascii=False), "vos.String")


def declared(type_info, write_kind=None, value=None):
    declaration = {"typeInfo": type_info}
    if write_kind:
        declaration["writeKind"] = write_kind
    if value is not None:
        declaration["value"] = value
    return declaration


def reduction(function, path, related_kind, property_name):
    return {"Scope": "PerInstance", "Function": function, "Path": path, "RelatedType": related_kind,
            "PropertyPath": property_name}


def formula(expression):
    return {"Scope": "PerInstance", "Expression": expression}


def judged_by(name, criteria):
    return {"Name": name, "Criteria": criteria, "EvaluationIntervalSeconds": 0}


class Model:
    def __init__(self):
        self.kit = Kit()
        self.is_ = self.kit.predicate("is")
        self.has = self.kit.predicate("has")
        self.kinds = {}
        self.configured = []

    def predicate(self, name, role=None):
        predicate = self.kit.predicate(name)
        return self.kit.bind(role, predicate) if role else predicate

    def kind(self, name, properties=None, derived=None, ranges=None, role=None):
        kind = self.kit.thing(identifier("kind", name), name, properties, archetype=True)
        if derived:
            self.kit.things[kind]["DerivedProperties"] = derived
        if ranges:
            self.kit.things[kind]["OwnRanges"] = {each["Name"]: each for each in ranges}
        if role:
            self.kit.bind(role, kind)
        self.configured.append(kind)
        self.kinds[name] = kind
        return kind

    def member(self, kind_name, name, stated=None, own=None):
        """`stated` holds values for names the kind declares, which a seed writes as an override under the kind;
        `own` holds properties only this Thing carries."""
        kind = self.kinds[kind_name]
        thing = self.kit.thing(identifier(kind_name, name), name, own)
        if stated:
            self.kit.things[thing]["InheritedOverrides"] = {
                kind: {"SourceId": kind, "SourceName": kind_name, "Properties": stated, "Inherited": None}}
        self.kit.rel(thing, self.is_, kind)
        return thing

    def relate(self, subject, predicate, target):
        self.kit.rel(subject, predicate, target)

    def place_under_roots(self):
        kit = self.kit
        roots = {}
        for name, role in playground.ROOT_ROLE.items():
            roots[name] = kit.bind(role, kit.thing(identifier("kind", name), name, archetype=True))
        for kind in self.configured:
            kit.rel(kind, self.is_, roots["Configuration"])
        binds = kit.thing(playground.stable_id(playground._TAG, "predicate", "binds"), "binds")
        for predicate in (binds, *sorted(kit.predicates)):
            kit.rel(predicate, self.is_, roots["Predicate"])
        role_kind = kit.thing(ROLE_ARCHETYPE, "Role", archetype=True)
        kit.rel(role_kind, self.is_, roots["Configuration"])
        for role in sorted(kit.role_things):
            kit.rel(role, self.is_, role_kind)


def declare_kinds(model):
    model.kind("Site", {
        "elevationMetres": declared("vos.Double", "FactOnly"),
        "rainfallMillimetres": declared("vos.Double", "ObservationOnly"),
        "utcOffsetSeconds": declared("vos.LongInteger", "FactOnly"),
    })
    model.kind("Catchment", {
        "areaHectares": declared("vos.Double", "FactOnly"),
        "rainfallMillimetresPerYear": declared("vos.Double", "ObservationOnly"),
        "siltDepthMetres": declared("vos.Double"),
        "surveyedAt": declared("vos.DateTime", "FactOnly"),
        "runoffCoefficient": declared("vos.Double", value=0.8),
        "lossFraction": declared("vos.Double", value=0.1),
        "dailyDemandM3": declared("vos.Double", value=40.0),
        "evaporationM3PerHectarePerYear": declared("vos.Double", value=3000.0),
        "irrigationM3PerHectarePerYear": declared("vos.Double", value=1500.0),
    }, derived={
        "storedM3": reduction("Sum", "has", "Reservoir", "quantityM3"),
        "capacityM3": reduction("Sum", "has", "Reservoir", "capacityM3"),
        "springFlowM3PerDay": reduction("Sum", "<-feeds", "Spring", "flowM3PerDay"),
        "harvestM3PerYear": formula("areaHectares * 10 * rainfallMillimetresPerYear * runoffCoefficient"),
        "irrigationM3PerYear": formula("areaHectares * irrigationM3PerHectarePerYear"),
        "supplyM3PerYear": formula("harvestM3PerYear * (1 - lossFraction) + springFlowM3PerDay * 365"),
        "balanceM3PerYear": formula("supplyM3PerYear - irrigationM3PerYear"),
        "surfaceBalanceM3PerYear": formula(
            "areaHectares * 10 * rainfallMillimetresPerYear - areaHectares * evaporationM3PerHectarePerYear"),
        "daysOfSupply": formula("storedM3 / dailyDemandM3"),
        "fillFraction": formula("storedM3 / capacityM3"),
    }, ranges=[
        judged_by("WaterResilient", f"daysOfSupply IS KNOWN AND daysOfSupply >= {TARGET_DAYS_OF_SUPPLY}"),
        judged_by("WaterShortOfTarget", f"daysOfSupply IS KNOWN AND daysOfSupply < {TARGET_DAYS_OF_SUPPLY}"),
        judged_by("WaterNotAssessed", "daysOfSupply IS UNKNOWN"),
    ])
    model.kind("Reservoir", {
        "capacityM3": declared("vos.Double", "FactOnly"),
        "quantityM3": declared("vos.Double", "ObservationOnly"),
    })
    model.kind("Spring", {
        "flowM3PerDay": declared("vos.Double", "FactOnly"),
        "decidedAt": declared("vos.DateTime", "FactOnly"),
    })
    model.kind("SpringDecision")


def declare_property_names(model):
    names_property_of = model.predicate("namesPropertyOf")
    model.kind("PropertyName", role="propertyName")
    no_bounds = None
    worded = (
        ("Catchment", "areaHectares", wording("catchment area", "Fläche des Einzugsgebiets", "مساحة المستجمع"),
         (1.0, 500.0, 0.5)),
        ("Catchment", "rainfallMillimetresPerYear", wording("yearly rainfall", "Jahresniederschlag", "الهطول السنوي"),
         no_bounds),
        ("Catchment", "runoffCoefficient", wording("share of rain that runs off", "Abflussbeiwert", "معامل الجريان"),
         (0.0, 1.0, 0.05)),
        ("Catchment", "lossFraction", wording("share lost on the way", "Verlustanteil", "نسبة الفاقد"),
         (0.0, 0.5, 0.01)),
        ("Catchment", "dailyDemandM3", wording("daily demand", "Tagesbedarf", "الطلب اليومي"), (5.0, 200.0, 5.0)),
        ("Catchment", "irrigationM3PerHectarePerYear",
         wording("irrigation per hectare", "Bewässerung je Hektar", "الري لكل هكتار"), (0.0, 6000.0, 100.0)),
        ("Reservoir", "quantityM3", wording("water held", "gespeichertes Wasser", "المياه المخزنة"), no_bounds),
        ("Reservoir", "capacityM3", wording("reservoir capacity", "Speicherkapazität", "سعة الخزان"), no_bounds),
        ("Spring", "flowM3PerDay", wording("spring flow", "Quellschüttung", "تدفق النبع"), no_bounds),
    )
    for kind_name, property_name, words, bounds in worded:
        properties = {"namedProperty": typed(property_name, "vos.String"), "wording": words}
        if bounds:
            properties.update({name: typed(value, "vos.Double")
                               for name, value in zip(("lowest", "highest", "stepSize"), bounds)})
        name = model.member("PropertyName", f"{kind_name} {property_name}", own=properties)
        model.relate(name, names_property_of, model.kinds[kind_name])


def declare_things(model):
    feeds = model.predicate("feeds")
    decided_as = model.predicate("decidedAs")

    def site(name, elevation):
        return model.member("Site", name, stated={"elevationMetres": typed(elevation, "vos.Double"),
                                              "utcOffsetSeconds": typed(3600, "vos.LongInteger")})

    def catchment(of_site, name, area, surveyed=None, silt=None):
        stated = {"areaHectares": typed(area, "vos.Double")}
        if surveyed:
            stated["surveyedAt"] = typed(surveyed, "vos.DateTime")
        if silt is not None:
            stated["siltDepthMetres"] = typed(silt, "vos.Double")
        thing = model.member("Catchment", name, stated=stated)
        model.relate(of_site, model.has, thing)
        return thing

    def reservoir(of_catchment, name, capacity):
        thing = model.member("Reservoir", name, stated={"capacityM3": typed(capacity, "vos.Double")})
        model.relate(of_catchment, model.has, thing)
        return thing

    def spring(into, name, flow):
        thing = model.member("Spring", name, stated={"flowM3PerDay": typed(flow, "vos.Double")})
        model.relate(thing, feeds, into)
        return thing

    alvik = site("Ålvik", 212.0)
    wadi = site("وادي النبع", 640.0)
    birchfield = site("Birchfield", 85.0)

    north = catchment(alvik, "Ålvik north slope", 42.0, surveyed="2026-03-02T09:00:00Z", silt=0.4)
    south = catchment(alvik, "Ålvik south slope", 18.5)
    terrace = catchment(wadi, "Upper terrace", 12.0, surveyed="2026-04-11T07:30:00Z")
    meadow = catchment(birchfield, "Birchfield meadow", 30.0)

    reservoir(north, "North pond", 3000.0)
    reservoir(north, "North cistern", 450.0)
    reservoir(south, "South pond", 1200.0)
    reservoir(terrace, "Terrace cistern", 600.0)
    reservoir(meadow, "Meadow pond", 2400.0)

    spring(north, "Cold spring", 14.0)
    spring(north, "Fern spring", 6.5)
    spring(terrace, "Rock spring", 3.0)
    capped_spring = spring(meadow, "Old spring", 0.5)

    decisions = {
        "tapped": wording("Tap", "Fassen", "استغلال"),
        "capped": wording("Cap", "Verschließen", "إغلاق"),
        "left": wording("Leave as it is", "Belassen", "ترك كما هو"),
    }
    decision_things = {name: model.member("SpringDecision", name, own={"wording": words})
                       for name, words in decisions.items()}
    model.relate(capped_spring, decided_as, decision_things["capped"])
    model.kit.things[capped_spring]["InheritedOverrides"][model.kinds["Spring"]]["Properties"]["decidedAt"] = typed(
        "2026-05-20T14:00:00Z", "vos.DateTime")


def declare_range_things(model):
    range_kind = model.kind("Range", role="range")
    judges = model.predicate("judges", role="judgedThing")
    ranges = {}
    for name, judged, criteria in (
        ("SpringAwaitingDecision", "Spring", "COUNT [decidedAs.SpringDecision] = 0"),
        ("SpringDecided", "Spring", "COUNT [decidedAs.SpringDecision] > 0"),
        ("CatchmentAwaitingSurvey", "Catchment", "surveyedAt IS UNKNOWN"),
    ):
        thing = model.kit.thing(identifier("range", name), name, {
            "Criteria": typed(criteria, "vos.String"),
            "EvaluationIntervalSeconds": typed(0, "vos.LongInteger"),
        })
        model.relate(thing, model.is_, range_kind)
        model.relate(thing, judges, model.kinds[judged])
        ranges[name] = thing
    return ranges


def declare_service_calls(model, ranges):
    """An echo service reached two ways: called for each catchment entering the state that awaits a survey,
    and at a subdomain a page's form and action list post to."""
    kit = model.kit
    service_kind = kit.archetype("Service")
    connection_kind = kit.archetype("Connection", "PlatformServiceConnection")
    model.configured += [service_kind, connection_kind]
    triggered_by = model.predicate("triggeredBy")
    watches = model.predicate("watches", role="stateWatch")
    completes_when = model.predicate("completesWhen", role="completesWhen")
    model.predicate("handledBy", role="callRecord")
    model.predicate("notifies")

    trigger_kind = model.kind("ConnectionTrigger")
    triggers = {}
    for name, role in (("http", "httpTrigger"), ("graph", "graphTrigger"), ("state", "stateTrigger")):
        triggers[name] = kit.bind(role, kit.thing(identifier("trigger", name), name))
        model.relate(triggers[name], model.is_, trigger_kind)

    completion_kind = model.kind("CallCompletion", role="callCompletion")
    when_the_service_answers = kit.bind("whenTheServiceAnswers", kit.thing(
        identifier("completion", "whenTheServiceAnswers"), "whenTheServiceAnswers",
        {"wording": wording("When the service answers", "Wenn der Dienst antwortet", "عندما تجيب الخدمة")}))
    model.relate(when_the_service_answers, model.is_, completion_kind)

    prototype = kit.thing(identifier("service", "Echo prototype"), "Echo prototype",
                        {"ServiceAssembly": typed("vos.Service.CSharp.Echo", "vos.String")}, archetype=True)
    model.relate(prototype, model.is_, service_kind)
    model.relate(prototype, completes_when, when_the_service_answers)
    service = kit.thing(identifier("service", "Echo service"), "Echo service", {
        "ServicePort": typed(ECHO_PORT, "vos.LongInteger"),
        "AutoStart": typed(False, "vos.Boolean"),
    })
    model.relate(service, model.is_, prototype)

    surveys = kit.thing(identifier("connection", "surveysCatchment"), "surveysCatchment",
                      {"callsAgainWhileItsThingStaysIn": typed(False, "vos.Boolean")})
    model.relate(surveys, model.is_, connection_kind)
    model.relate(surveys, model.has, service)
    model.relate(surveys, triggered_by, triggers["state"])
    model.relate(surveys, watches, ranges["CatchmentAwaitingSurvey"])

    echo = kit.thing(identifier("connection", ECHO_SUBDOMAIN), ECHO_SUBDOMAIN,
                   {"Subdomain": typed(ECHO_SUBDOMAIN, "vos.String")})
    model.relate(echo, model.is_, connection_kind)
    model.relate(echo, model.has, service)
    model.relate(echo, triggered_by, triggers["http"])


WATER_VERDICTS = [
    {"state": "WaterResilient", "reads": "{value} of supply — clears the {target} target"},
    {"state": "WaterShortOfTarget", "reads": "{value} of supply — short of the {target} target",
     "levers": {"raise": "more {term}", "lower": "less {term}"}},
    {"state": "WaterNotAssessed", "reads": "not assessed — no reservoir reports the water it holds"},
]
ORIGIN_WORDS = {"stated": "stated by the surveyor", "measured": "measured", "assumed": "the platform's assumption",
                "unknown": "unrecorded"}
TO_CATCHMENTS = [{"predicate": "has", "archetype": "Catchment"}]
DAILY_DECISIONS = {"kind": "timeseries", "archetype": "Spring", "happenedAt": "decidedAt", "reduction": "count",
                   "bucketSeconds": 86400, "buckets": 14}


def origin_of(property_name):
    return {"kind": "origin", "property": property_name, "via": TO_CATCHMENTS, "reads": ORIGIN_WORDS}


def catchment_figure(title, property_name, unit=None, number_format="decimal1"):
    figure = {"type": "kpi", "title": title, "format": number_format,
              "value": {"kind": "related", "via": TO_CATCHMENTS, "property": property_name},
              "origin": origin_of(property_name)}
    if unit:
        figure["unit"] = unit
    return figure


def catchments_page():
    return {
        "title": "Catchments",
        "subtitle": "What each site's catchments hold and supply",
        "icon": "droplet",
        "compare": {"label": "site", "archetype": "Site"},
        "sections": [
            {"title": "At a glance", "layout": "kpi-strip", "widgets": [
                {"type": "kpi", "title": "Catchments", "format": "integer",
                 "value": {"kind": "aggregate", "archetype": "Catchment", "reduction": "count",
                           "scope": {"viaPredicate": "has"}}},
                {"type": "kpi", "title": "Short of the target", "format": "integer", "direction": "down-good",
                 "value": {"kind": "stateCount", "state": "WaterShortOfTarget", "archetype": "Catchment"}},
                {"type": "kpi", "title": "Target", "format": "integer", "unit": "days",
                 "value": {"kind": "const", "value": TARGET_DAYS_OF_SUPPLY}},
                {"type": "kpi", "title": "Reservoirs filled", "format": "percent",
                 "value": {"kind": "ratio",
                           "numerator": {"kind": "aggregate", "archetype": "Reservoir", "reduction": "sum",
                                         "property": "quantityM3"},
                           "denominator": {"kind": "aggregate", "archetype": "Reservoir", "reduction": "sum",
                                           "property": "capacityM3"}}},
                {"type": "kpi", "title": "Elevation", "format": "integer", "unit": "m",
                 "value": {"kind": "property", "thing": "$scope", "property": "elevationMetres"},
                 "origin": {"kind": "origin", "property": "elevationMetres", "reads": ORIGIN_WORDS}},
                {"type": "kpi", "title": "Springs decided today", "format": "integer",
                 "value": {"kind": "latest", "series": DAILY_DECISIONS}, "spark": DAILY_DECISIONS},
                {"type": "kpi", "title": "Service answer",
                 "value": {"kind": "service", "endpoint": f"/api/endpoints/{ECHO_SUBDOMAIN}",
                           "body": {"about": "$scope"},
                           "select": "echo"}},
            ]},
            {"title": "Water", "layout": "split", "widgets": [
                {"type": "verdict", "title": "Balance", "rows": [
                    {"label": "Water", "format": "decimal1", "unit": "days",
                     "verdicts": {"kind": "verdict", "via": TO_CATCHMENTS, "states": WATER_VERDICTS}}]},
                {"type": "working", "title": "How it was worked out", "rows": [
                    {"label": "Days of supply", "format": "decimal1", "unit": "days",
                     "value": {"kind": "related", "via": TO_CATCHMENTS, "property": "daysOfSupply"},
                     "working": {"kind": "working", "property": "daysOfSupply", "via": TO_CATCHMENTS}},
                    {"label": "Yearly balance", "format": "integer", "unit": "m³",
                     "value": {"kind": "related", "via": TO_CATCHMENTS, "property": "balanceM3PerYear"},
                     "working": {"kind": "working", "property": "balanceM3PerYear", "via": TO_CATCHMENTS}}]},
            ]},
            {"title": "Where the figures came from", "layout": "kpi-strip", "widgets": [
                catchment_figure("Area", "areaHectares", "ha"),
                catchment_figure("Yearly rainfall", "rainfallMillimetresPerYear", "mm", "integer"),
                catchment_figure("Runoff", "runoffCoefficient", number_format="percent"),
                catchment_figure("Silt depth", "siltDepthMetres", "m"),
            ]},
            {"title": "Catchments", "layout": "single", "widgets": [
                {"type": "table", "title": "Every catchment", "rowDetail": True,
                 "columns": [{"key": "name", "label": "Catchment"},
                             {"key": "site", "label": "Site"},
                             {"key": "areaHectares", "label": "Area", "numeric": True, "format": "decimal1"},
                             {"key": "condition", "label": "Water", "render": "badge"}],
                 "rows": {"kind": "thingList", "archetype": "Catchment", "scope": {"viaPredicate": "has"},
                          "computed": [
                              {"key": "site",
                               "value": {"kind": "related", "via": [{"predicate": "has", "direction": "in"}]}},
                              {"key": "condition",
                               "value": {"kind": "stateOf",
                                         "states": ["WaterShortOfTarget", "WaterResilient", "WaterNotAssessed"]}}]}},
                {"type": "table", "title": "Awaiting a survey",
                 "columns": [{"key": "name", "label": "Catchment"},
                             {"key": "areaHectares", "label": "Area", "numeric": True, "format": "decimal1"}],
                 "rows": {"kind": "stateList", "state": "CatchmentAwaitingSurvey", "archetype": "Catchment",
                          "properties": ["areaHectares"]}},
                {"type": "leaderboard", "title": "Sites", "labelKey": "name",
                 "entities": {"kind": "compareEntities", "properties": ["elevationMetres"],
                              "computed": [{"key": "catchments",
                                            "value": {"kind": "aggregate", "archetype": "Catchment",
                                                      "reduction": "count", "scope": {"viaPredicate": "has"}}}]},
                 "metrics": [{"key": "catchments", "label": "Catchments", "format": "integer"},
                             {"key": "elevationMetres", "label": "Elevation", "format": "integer"}]},
                {"type": "lineSeries", "title": "Rainfall by month", "unit": "mm", "series": [
                    {"label": "Rainfall",
                     "value": {"kind": "history", "property": "rainfallMillimetres", "windowSeconds": 31536000,
                               "steps": [{"fold": "month", "function": "Sum"}]}}]},
            ]},
            {"title": "Springs", "layout": "split", "widgets": [
                {"type": "action", "title": "Springs waiting for a decision",
                 "hint": "Each button is a decision this model holds.",
                 "rows": {"kind": "stateList", "state": "SpringAwaitingDecision", "archetype": "Spring",
                          "properties": ["flowM3PerDay"],
                          "computed": [{"key": "feeds",
                                        "value": {"kind": "related", "via": [{"predicate": "feeds"}]}}]},
                 "shows": ["flowM3PerDay", "feeds"],
                 "writes": {
                     "choicesFrom": {"from": {"kind": "thingList", "archetype": "SpringDecision"},
                                     "predicate": "decidedAs"},
                     "stamps": "decidedAt",
                     "choices": [
                         {"label": "Tap for the reservoir", "target": "tapped"},
                         {"label": "Ask for a flow test", "act": "flowTest", "via": ECHO_SUBDOMAIN,
                          "asks": [{"key": "days", "label": "Days to measure", "kind": "number"},
                                   {"key": "reservoir", "label": "Reservoir to compare with", "kind": "choice",
                                    "options": {"kind": "thingList", "archetype": "Reservoir"},
                                    "shows": ["capacityM3"]},
                                   {"key": "note", "label": "Note", "optional": True}]},
                         {"label": "Record as reviewed", "act": "reviewed", "via": "/api/operator-activity"},
                     ]},
                 "decided": {
                     "rows": {"kind": "stateList", "state": "SpringDecided", "archetype": "Spring",
                              "properties": ["decidedAt"],
                              "computed": [{"key": "decision",
                                            "value": {"kind": "related", "via": [{"predicate": "decidedAs"}]}}]},
                     "decision": "decision"}},
                {"type": "form", "title": "Report a spring", "submit": "Send the report",
                 "fields": [{"key": "name", "label": "Name of the spring"},
                            {"key": "flowM3PerDay", "label": "Flow in cubic metres a day", "kind": "number"},
                            {"key": "catchment", "label": "Catchment it feeds", "kind": "choice",
                             "options": {"kind": "thingList", "archetype": "Catchment"}},
                            {"key": "seenAt", "label": "When it was seen", "kind": "datetime", "optional": True}],
                 "preview": {"act": "checkSpring", "label": "Check the report"},
                 "writes": {"via": ECHO_SUBDOMAIN, "act": "reportSpring", "archetype": "Spring"}},
            ]},
        ],
        "detail": {"relations": [{"predicate": "has", "properties": "*"},
                                 {"predicate": "feeds", "direction": "in", "properties": ["flowM3PerDay"]}]},
        "translations": {"de": {"Catchments": "Einzugsgebiete", "Water": "Wasser", "Springs": "Quellen"}},
    }


UNREADABLE_PAGE = "Reservoir levels"
UNREADABLE_DESCRIPTION = '{"title": "Reservoir levels", "sections": ['
# The platform's check of these seeds expects a seed with this file beside it to be refused with exactly these errors.
REFUSALS_OF_THE_UNREADABLE_PAGE = f"Dashboard '{UNREADABLE_PAGE}': its 'specification' is not valid JSON\n"


def declare_pages(model, with_an_unreadable_page):
    kit = model.kit
    page_kind = model.kind("Dashboard", role="page")
    descriptions = {"Catchments": json.dumps(catchments_page(), ensure_ascii=False)}
    if with_an_unreadable_page:
        descriptions[UNREADABLE_PAGE] = UNREADABLE_DESCRIPTION
    for name, description in descriptions.items():
        page = kit.thing(identifier("page", name), name, {"specification": typed(description, "vos.String")})
        model.relate(page, model.is_, page_kind)

    kit.bind("displaySettings", kit.thing(identifier("settings", "GUI_Settings"), "GUI_Settings", {
        "ClassifyingProperty": typed("", "vos.String"),
        "ModelLoadProperties": typed("", "vos.String"),
    }))


def build(with_an_unreadable_page=False):
    model = Model()
    declare_kinds(model)
    declare_property_names(model)
    declare_things(model)
    ranges = declare_range_things(model)
    declare_service_calls(model, ranges)
    declare_pages(model, with_an_unreadable_page)
    model.place_under_roots()
    return model.kit


def as_seed(kit):
    return {
        "Id": identifier("seed"),
        "Name": SEED_NAME,
        "Things": list(kit.things.values()),
        "Relationships": list(kit.rels.values()),
    }


def committed_seeds():
    """Each committed file and the seed it holds. The second carries a page whose description cannot be read,
    which the seed validator refuses, so it is kept apart from the seed that has to pass."""
    return {
        COMMITTED_SEED: as_seed(build()),
        COMMITTED_SEED_WITH_AN_UNREADABLE_PAGE: as_seed(build(with_an_unreadable_page=True)),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", metavar="DIRECTORY", default=str(COMMITTED_SEED.parent),
                        help="the folder to write both seeds into")
    args = parser.parse_args()
    folder = Path(args.out)
    folder.mkdir(parents=True, exist_ok=True)
    for committed, seed in committed_seeds().items():
        path = folder / committed.name
        playground.dump_seed(seed, path)
        print(f"Wrote {path}: {len(seed['Things'])} things, {len(seed['Relationships'])} relationships.")
    refusals = folder / COMMITTED_SEED_WITH_AN_UNREADABLE_PAGE.name.replace(".json", ".refusals.txt")
    refusals.write_text(REFUSALS_OF_THE_UNREADABLE_PAGE, encoding="utf-8")
    print(f"Wrote {refusals}.")


if __name__ == "__main__":
    main()
