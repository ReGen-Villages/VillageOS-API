#!/usr/bin/env python3
"""Generate example DAGs + services for the Trellis Pipeline page and drop them into any model.

The content lives in `catalog.py`; this file turns it into VOS Things + Relationships in the seed JSON
shape (mirrors vos.Infrastructure.Tests/Fixtures/pipeline-demo.seed.json) and either writes a standalone
seed fragment or merges it into an existing seed in place.

    # standalone fragment (load on its own, or inspect the shape)
    python3 generate.py --out PipelinePlayground.seed.json

    # drop the playground into any model seed (idempotent; keeps a .bak)
    python3 generate.py --into ../../path/to/Some.seed.json

    # ...with per-model ids so the same DAGs in two seeds don't collide in one broker
    python3 generate.py --into ../../path/to/Some.seed.json --namespace SomeModel

    # take it back out again
    python3 generate.py --into ../../path/to/Some.seed.json --remove

Idempotent by construction: every id is a deterministic UUIDv5 of a stable key, so a re-run replaces the
same Things instead of forking duplicates. When merging, the target's own vocabulary is reused rather than
doubled: the built-in `is`/`has`/`of`/`binds` predicates are matched by name, and each archetype or predicate
playing a role by what the target's role Thing binds — so a model that renamed one keeps its own.
"""

import argparse
import json
import shutil
import sys
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from catalog import SERVICES, PIPELINES, MESSAGE_KINDS, EXTERNAL_SYSTEMS, CADENCES

# Shared with tools/simulator so ids are consistent across the ecosystem. With no --namespace this root is
# used directly, keeping the standalone fragment's ids stable. --namespace NAME derives a per-model root
# (uuid5 of NAME under this root), so the same DAGs merged into two different model seeds get disjoint ids
# and can coexist in one broker rather than colliding.
_ROOT_NAMESPACE = uuid.UUID("6f9b1e2c-4a7d-5b8e-9c0f-1d2e3a4b5c6d")
_id_namespace = _ROOT_NAMESPACE
_TAG = "pipeline-playground"

# The Trellis editor and the Phloem orchestrator find each role Thing by this identifier and follow what it
# binds; nothing reads the archetype and predicate names below, so a model may rename any of them.
ROLE_NAMESPACE = uuid.UUID("c33fcf16-6706-4f93-bfa5-8997bfaaf4bd")


def role_id(word):
    return str(uuid.uuid5(ROLE_NAMESPACE, word))


# Every role Thing `is` this archetype, so a lookup by name passes over a role named like the Thing it binds.
ROLE_ARCHETYPE = role_id("role")


def role_name(word):
    return word[0].upper() + word[1:]


ARCHETYPE_ROLE = {
    "Pipeline": "pipeline",
    "PipelineNode": "pipelineNode",
    "PipelineInput": "pipelineInput",
    "PipelineOutput": "pipelineOutput",
    "Port": "port",
    "Service": "service",
    "Connection": "connection",
    "PipelineWire": "pipelineWire",
    "PipelineRun": "pipelineRun",
    "NodeRun": "nodeRun",
    "ExternalSystem": "externalSystem",
    "MessageKind": "messageKind",
    "SentMessage": "sentMessage",
    "Cadence": "cadence",
}

PREDICATE_ROLE = {
    "triggeredBy": "trigger",
    "sends": "sends",
    "told": "told",
    "toldThrough": "toldThrough",
    "arrivesAt": "arrivesAt",
    "standsFor": "standsFor",
    "about": "runSubject",
    "offers": "choice",
    "produced": "producedThing",
    "inputConnector": "inputConnector",
    "outputConnector": "outputConnector",
    "takenOn": "takenOn",
}

# The two results a wire is taken on, and the error port every service node has without declaring it.
RESULT_ROLE = {"success": "success", "fail": "fail"}
ERROR_PORT_ROLE = "errorPort"

# The three roots every archetype sits under: what a model sets up, what then happens there, and what a
# relationship runs along.
ROOT_ROLE = {
    "Configuration": "configuration",
    "Operation": "operation",
    "Predicate": "predicate",
}

# The platform recognises a trigger as the Thing the role for that word binds, never by the trigger's name.
TRIGGER_ROLE = {
    "http": "httpTrigger",
    "graph": "graphTrigger",
}


def set_namespace(name):
    """Rebind the id namespace to a per-model root so merged DAG ids are distinct per seed. An empty or
    absent name restores the shared root (the standalone identity)."""
    global _id_namespace
    _id_namespace = uuid.uuid5(_ROOT_NAMESPACE, name) if name else _ROOT_NAMESPACE

# Canvas grid → pixel coordinates for a node's x/y (the Pipeline page round-trips these).
_COL_WIDTH = 260
_ROW_HEIGHT = 150
_ORIGIN = 60


def stable_id(*parts):
    return str(uuid.uuid5(_id_namespace, "/".join(str(p) for p in parts)))


def typed(value, type_info):
    return {"typeInfo": type_info, "value": value}


class Kit:
    """Accumulates the playground's Things and Relationships, keyed by deterministic id so the build is
    idempotent. `shared` holds the generic vocabulary (is/has/of/binds + the pipeline archetypes) that is matched
    against a target model's own at merge time rather than blindly duplicated."""

    def __init__(self):
        self.things = {}          # id -> Thing dict
        self.rels = {}            # dedup key -> Relationship dict
        self.shared = set()       # ids of the vocabulary reconciled against a target model
        self.role_of = {}         # id of a Thing playing a role -> the role's id (how a target's copy is found)
        self.role_things = set()  # ids of the role Things, which a target holds under the same ids or not at all
        self.predicates = set()   # ids of every predicate declared, whether or not a relationship runs along it
        self._name = {}           # id -> Name (for readable relationship labels)

    def thing(self, tid, name, properties=None, shared=False, archetype=False):
        if tid not in self.things:
            self.things[tid] = {"Id": tid, "Name": name, "Properties": properties or {}}
            if archetype:
                self.things[tid]["IsArchetype"] = True
            self._name[tid] = name
            if shared:
                self.shared.add(tid)
        return tid

    def rel(self, subject, predicate, target):
        key = (subject, predicate, target)
        if key not in self.rels:
            name = f"{self._name.get(subject, '?')} {self._name.get(predicate, '?')} {self._name.get(target, '?')}"
            self.rels[key] = {
                "Id": stable_id(_TAG, "rel", *(str(k) for k in key)),
                "Name": name,
                "Subject": subject, "Predicate": predicate, "Target": target,
                "Properties": {},
            }
        return self.rels[key]

    def bind(self, word, tid):
        """The role Thing for `word`, binding the Thing that plays it."""
        binds = self.thing(stable_id(_TAG, "predicate", "binds"), "binds", shared=True)
        role = role_id(word)
        self.thing(role, role_name(word))
        self.role_things.add(role)
        self.rel(role, binds, tid)
        self.role_of[tid] = role
        return tid

    def predicate(self, name):
        """A predicate found on merge by the role it plays, or by name: the built-in `is`, `has` and `of`, or
        one a target already holds under the name, which then plays the role as it reconciles."""
        tid = self.thing(stable_id(_TAG, "predicate", name), name, shared=True)
        self.predicates.add(tid)
        return self.bind(PREDICATE_ROLE[name], tid) if name in PREDICATE_ROLE else tid

    def archetype(self, role, name=None):
        """An archetype that says what it is through the role Thing that binds it. The name is this
        generator's own choice and defaults to the role; nothing reads it."""
        name = name or role
        tid = self.thing(stable_id(_TAG, "archetype", name), name, shared=True, archetype=True)
        return self.bind(ARCHETYPE_ROLE[role], tid)


def port_of(port_ids, node_id, service_id, name, direction):
    """A port the node holds itself wins over its service's, as the orchestrator lists a node's own ports first."""
    return port_ids.get((node_id, name, direction)) or port_ids[(service_id, name, direction)]


def build():
    k = Kit()

    is_ = k.predicate("is")
    has = k.predicate("has")
    of = k.predicate("of")
    carries = k.predicate("carries")
    triggered_by = k.predicate("triggeredBy")
    sends = k.predicate("sends")
    told = k.predicate("told")
    told_through = k.predicate("toldThrough")
    arrives_at = k.predicate("arrivesAt")
    stands_for = k.predicate("standsFor")
    offers = k.predicate("offers")
    input_connector = k.predicate("inputConnector")
    output_connector = k.predicate("outputConnector")
    taken_on = k.predicate("takenOn")
    # Declared for the orchestrator, which relates a run to its subject and a node run to each Thing the node
    # produced.
    k.predicate("about")
    k.predicate("produced")

    Pipeline = k.archetype("Pipeline")
    PipelineNode = k.archetype("PipelineNode")
    PipelineInput = k.archetype("PipelineInput")
    PipelineOutput = k.archetype("PipelineOutput")
    Port = k.archetype("Port")
    Service = k.archetype("Service")
    Connection = k.archetype("Connection", "PlatformServiceConnection")
    PipelineWire = k.archetype("PipelineWire")
    PipelineRun = k.archetype("PipelineRun")
    NodeRun = k.archetype("NodeRun")
    ExternalSystem = k.archetype("ExternalSystem")
    MessageKind = k.archetype("MessageKind")
    SentMessage = k.archetype("SentMessage")
    Cadence = k.archetype("Cadence")

    k.rel(carries, is_, PipelineWire)
    # Every service's connection is a door reached over HTTP at its subdomain, which is what makes it a catalyst
    # the Pipelines page lists; a system's told-through connection is written along instead. The trigger is a
    # Thing the connection relates to, as the platform reads it.
    http = k.thing(stable_id(_TAG, "trigger", "http"), "http", shared=True)
    graph = k.thing(stable_id(_TAG, "trigger", "graph"), "graph", shared=True)
    k.bind(TRIGGER_ROLE["http"], http)
    k.bind(TRIGGER_ROLE["graph"], graph)
    k.rel(PipelineInput, is_, PipelineNode)  # boundary nodes are pipeline nodes too
    k.rel(PipelineOutput, is_, PipelineNode)

    results = {word: k.bind(role, k.thing(stable_id(_TAG, "result", word), word, shared=True)) for word, role in RESULT_ROLE.items()}
    error_port = k.thing(stable_id(_TAG, "port", "error"), "error", {
        "portName": typed("error", "vos.String"),
        "direction": typed("out", "vos.String"),
        "type": typed("string", "vos.String"),
    }, shared=True)
    k.rel(error_port, is_, Port)
    k.bind(ERROR_PORT_ROLE, error_port)

    port_ids = {}

    def make_port(owner_id, owner_key, name, direction, type, required, collection):
        pid = stable_id(_TAG, "port", owner_key, name, direction)
        port_ids[(owner_id, name, direction)] = pid
        props = {
            "portName": typed(name, "vos.String"),
            "direction": typed(direction, "vos.String"),
            "type": typed(type, "vos.String"),
            "required": typed(required, "vos.Boolean"),
        }
        if collection:
            props["collection"] = typed(True, "vos.Boolean")
        k.thing(pid, f"{owner_key}.{direction}.{name}", props)
        k.rel(pid, is_, Port)
        k.rel(owner_id, has, pid)
        return pid

    # Each service → a Service (declaring its ports) wrapped in a PlatformServiceConnection (the palette
    # entry, carrying the Subdomain it is called at).
    connection_by_key = {}
    service_by_key = {}
    offered_by_key = {}
    strategy_kind = k.thing(stable_id(_TAG, "kind", "Strategy"), "Strategy", archetype=True)
    for svc in SERVICES:
        svc_id = k.thing(stable_id(_TAG, "service", svc["key"]), f"{svc['label']} service",
                         {"rehearses": typed(svc.get("rehearses", True), "vos.Boolean")})
        service_by_key[svc["key"]] = svc_id
        k.rel(svc_id, is_, Service)
        for (name, direction, type, required, collection) in svc["ports"]:
            make_port(svc_id, svc["key"], name, direction, type, required, collection)
        # What an in port offers is related to the port itself, so every node bound to the service may choose
        # among the same Things.
        for port_name, names in svc.get("offers", {}).items():
            for name in names:
                offered_id = k.thing(stable_id(_TAG, "offered", svc["key"], port_name, name), name)
                k.rel(offered_id, is_, strategy_kind)
                k.rel(port_ids[(svc_id, port_name, "in")], offers, offered_id)
                offered_by_key[(svc["key"], port_name, name)] = offered_id

        conn_id = k.thing(stable_id(_TAG, "connection", svc["key"]), svc["label"],
                          {"Subdomain": typed(svc["subdomain"], "vos.String")})
        k.rel(conn_id, is_, Connection)
        k.rel(conn_id, has, svc_id)
        k.rel(conn_id, triggered_by, http)
        connection_by_key[svc["key"]] = conn_id

    # Each kind of message → a MessageKind arriving at the door that reads it; each external system → an
    # ExternalSystem that sends and is told its kinds.
    kind_by_key = {}
    for (key, label, arrives_at_service) in MESSAGE_KINDS:
        kind_id = k.thing(stable_id(_TAG, "message-kind", key), label)
        k.rel(kind_id, is_, MessageKind)
        if arrives_at_service:
            k.rel(kind_id, arrives_at, connection_by_key[arrives_at_service])
        kind_by_key[key] = kind_id
    system_by_key = {}
    for system in EXTERNAL_SYSTEMS:
        system_id = k.thing(stable_id(_TAG, "external-system", system["key"]), system["label"])
        k.rel(system_id, is_, ExternalSystem)
        for key in system["sends"]:
            k.rel(system_id, sends, kind_by_key[key])
        for key in system["told"]:
            k.rel(system_id, told, kind_by_key[key])
        # A system that is told something names the connection its messages go out through: a predicate the
        # orchestrator writes a sent message along, bound to the service that sends it.
        if system.get("toldThrough"):
            through_id = k.thing(stable_id(_TAG, "told-through", system["key"]), f"tells {system['label']}")
            k.rel(through_id, is_, Connection)
            k.rel(through_id, triggered_by, graph)
            k.rel(through_id, has, service_by_key[system["toldThrough"]])
            k.rel(system_id, told_through, through_id)
        system_by_key[system["key"]] = system_id

    # lastDue is declared empty because the orchestrator's write updates a property and never creates one.
    cadence_by_key = {}
    for cadence in CADENCES:
        cadence_id = k.thing(stable_id(_TAG, "cadence", cadence["key"]), cadence["label"], {
            "intervalMinutes": typed(float(cadence["intervalMinutes"]), "vos.Double"),
            "lastDue": typed(None, "vos.DateTime"),
        })
        k.rel(cadence_id, is_, Cadence)
        cadence_by_key[cadence["key"]] = cadence_id

    stood_for = {"messageKind": kind_by_key, "externalSystem": system_by_key, "cadence": cadence_by_key,
                 "pipeline": {pipe["name"]: stable_id(_TAG, "pipeline", pipe["name"]) for pipe in PIPELINES}}

    for pipe in PIPELINES:
        pipe_id = k.thing(stable_id(_TAG, "pipeline", pipe["name"]), pipe["name"], {"published": typed(True, "vos.Boolean")})
        k.rel(pipe_id, is_, Pipeline)

        node_id = {}
        service_of = {}
        for node in pipe["nodes"]:
            nid = stable_id(_TAG, "node", pipe["name"], node["key"])
            node_id[node["key"]] = nid
            if "service" in node:
                service_of[node["key"]] = service_by_key[node["service"]]
            x = _ORIGIN + node["at"]["col"] * _COL_WIDTH
            y = _ORIGIN + node["at"]["row"] * _ROW_HEIGHT
            props = {"x": typed(float(x), "vos.Double"), "y": typed(float(y), "vos.Double")}
            if node.get("params"):
                props["parameterBindings"] = typed(json.dumps(node["params"]), "vos.String")
            if node.get("onItemError"):
                props["onItemError"] = typed(node["onItemError"], "vos.String")

            if "service" in node:
                label = node.get("label", next(s["label"] for s in SERVICES if s["key"] == node["service"]))
                k.thing(nid, label, props)
                k.rel(nid, is_, PipelineNode)
                k.rel(nid, has, connection_by_key[node["service"]])
                for (pname, ptype) in node.get("outPorts", []):
                    make_port(nid, f"{pipe['name']}.{node['key']}", pname, "out", ptype, False, False)
                if node.get("chooses"):
                    port_name, name = node["chooses"]
                    k.rel(nid, offers, offered_by_key[(node["service"], port_name, name)])
            else:
                is_input = "input" in node
                label = node.get("label", "Input" if is_input else "Output")
                k.thing(nid, label, props)
                k.rel(nid, is_, PipelineNode)
                k.rel(nid, is_, PipelineInput if is_input else PipelineOutput)
                direction = "out" if is_input else "in"
                for spec in node["input" if is_input else "output"]:
                    pname, ptype = spec[0], spec[1]
                    prequired = spec[2] if len(spec) > 2 else False
                    make_port(nid, f"{pipe['name']}.{node['key']}", pname, direction, ptype, prequired, False)
                if node.get("standsFor"):
                    kind, key = node["standsFor"]
                    k.rel(nid, stands_for, stood_for[kind][key])

            k.rel(pipe_id, has, nid)

        # Every value is declared, empty where unset, because a property write on a Thing updates and does not
        # create, so the page could never set one left out.
        for wire in pipe["wires"]:
            source, target = node_id[wire["from"]], node_id[wire["to"]]
            wid = stable_id(_TAG, "wire", pipe["name"], wire["from"], wire["fromPort"], wire["to"], wire["toPort"], wire.get("toPath", ""))
            k.thing(wid, f"{k.things[source]['Name']}.{wire['fromPort']} to {k.things[target]['Name']}.{wire['toPort']}",
                    {name: typed(wire.get(name, ""), "vos.String") for name in ("fromPath", "toPath", "transform")})
            k.rel(wid, is_, PipelineWire)
            k.rel(source, has, wid)
            k.rel(wid, carries, target)
            out_port = error_port if wire["fromPort"] == "error" else port_of(port_ids, source, service_of.get(wire["from"]), wire["fromPort"], "out")
            k.rel(wid, input_connector, out_port)
            k.rel(wid, output_connector, port_of(port_ids, target, service_of.get(wire["to"]), wire["toPort"], "in"))
            for taken in wire.get("takenOn", ["success"]):
                k.rel(wid, taken_on, results[taken])

        # Seeded run history: a PipelineRun -of-> pipeline, each carrying NodeRuns so the History
        # panel and node status rings show data with no live Phloem. A fan-out node also gets per-item NodeRuns
        # (carrying index/total) that drive its progress count.
        for run_index, run in enumerate(pipe.get("runs", [])):
            run_id = stable_id(_TAG, "run", pipe["name"], run_index)
            k.thing(run_id, f"{pipe['name']} run {run_index + 1}", {
                "status": typed(run["status"], "vos.String"),
                "startedUtc": typed(run["startedUtc"], "vos.String"),
            })
            k.rel(run_id, is_, PipelineRun)
            k.rel(run_id, of, pipe_id)
            for nr in run["nodes"]:
                target = node_id[nr["node"]]
                agg_id = stable_id(_TAG, "noderun", pipe["name"], run_index, nr["node"])
                k.thing(agg_id, f"{nr['node']} @ run {run_index + 1}", {
                    "nodeId": typed(target, "vos.String"),
                    "status": typed(nr["status"], "vos.String"),
                })
                k.rel(agg_id, is_, NodeRun)
                k.rel(run_id, has, agg_id)
                fan = nr.get("fanout")
                if fan:
                    for item in range(fan["total"]):
                        item_id = stable_id(_TAG, "noderun-item", pipe["name"], run_index, nr["node"], item)
                        status = "succeeded" if item < fan["done"] else "failed"
                        k.thing(item_id, f"{nr['node']} item {item} @ run {run_index + 1}", {
                            "nodeId": typed(target, "vos.String"),
                            "status": typed(status, "vos.String"),
                            "index": typed(item, "vos.LongInteger"),
                            "total": typed(fan["total"], "vos.LongInteger"),
                        })
                        k.rel(item_id, is_, NodeRun)
                        k.rel(run_id, has, item_id)

    # A target that already binds the roots is reused through the roles, like the rest of the shared vocabulary.
    Configuration = k.bind(ROOT_ROLE["Configuration"],
                           k.thing(stable_id(_TAG, "archetype", "Configuration"), "Configuration", shared=True, archetype=True))
    Operation = k.bind(ROOT_ROLE["Operation"],
                       k.thing(stable_id(_TAG, "archetype", "Operation"), "Operation", shared=True, archetype=True))
    Predicate = k.bind(ROOT_ROLE["Predicate"],
                       k.thing(stable_id(_TAG, "archetype", "Predicate"), "Predicate", shared=True, archetype=True))
    binds = k.thing(stable_id(_TAG, "predicate", "binds"), "binds", shared=True)
    for configured in (Pipeline, PipelineNode, Port, Service, Connection, PipelineWire, ExternalSystem,
                       MessageKind, Cadence, strategy_kind):
        k.rel(configured, is_, Configuration)
    for operational in (PipelineRun, NodeRun, SentMessage):
        k.rel(operational, is_, Operation)
    for predicate in (binds, *sorted(k.predicates)):
        k.rel(predicate, is_, Predicate)
    Role = k.thing(ROLE_ARCHETYPE, "Role", archetype=True)
    k.rel(Role, is_, Configuration)
    for role in sorted(k.role_things):
        k.rel(role, is_, Role)
    return k


def load_seed(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def dump_seed(seed, path):
    """Write in the seed house style — top-level keys in their existing order, with the Things and
    Relationships arrays one compact object per line. Existing entities re-serialize byte-for-byte, so a
    merge only adds lines rather than reformatting the whole file."""
    with open(path, "w", encoding="utf-8") as f:
        f.write("{\n")
        keys = list(seed.keys())
        for ki, key in enumerate(keys):
            tail = "" if ki == len(keys) - 1 else ","
            value = seed[key]
            if key in ("Things", "Relationships") and isinstance(value, list):
                f.write(f"  {json.dumps(key)}: [\n")
                for ri, entity in enumerate(value):
                    f.write("    " + json.dumps(entity) + ("" if ri == len(value) - 1 else ",") + "\n")
                f.write(f"  ]{tail}\n")
            else:
                f.write(f"  {json.dumps(key)}: {json.dumps(value)}{tail}\n")
        f.write("}\n")


def as_fragment(kit):
    return {
        "Id": stable_id(_TAG, "seed"),
        "Name": "PipelinePlayground",
        "Things": list(kit.things.values()),
        "Relationships": list(kit.rels.values()),
    }


def _roles_in(seed):
    """The Things directly `is` Role, as the platform's seed validation requires of a role."""
    is_ids = {t["Id"] for t in seed["Things"] if t["Name"] == "is"}
    return {r.get("Subject") for r in seed["Relationships"]
            if r.get("Predicate") in is_ids and r.get("Target") == ROLE_ARCHETYPE}


def merge_into(kit, seed):
    """Merge the kit into a loaded seed in place, reusing the target's own vocabulary instead of duplicating
    it: a Thing playing a role is found through what the target's role Thing binds, so a model that renamed
    one keeps its name, and the built-in `is`/`has`/`of`/`binds` predicates are found by name. A Thing the
    target has under the same name but has never bound comes to play the role as it reconciles, because the
    binding is the only thing that says what it is for. A role Thing is matched by its identifier alone and
    a target's own is kept, since every model declares it under the same one."""
    seed.setdefault("Things", [])
    seed.setdefault("Relationships", [])
    roles = _roles_in(seed)
    by_name = {}
    for t in seed["Things"]:
        if t["Id"] not in roles:
            by_name.setdefault(t["Name"], t["Id"])
    binds_ids = {t["Id"] for t in seed["Things"] if t["Name"] == "binds"}
    bound_by_role = {}
    for r in seed["Relationships"]:
        if r.get("Predicate") in binds_ids:
            bound_by_role.setdefault(r.get("Subject"), r.get("Target"))
    thing_ids = {t["Id"] for t in seed["Things"]}
    rel_keys = {(r.get("Subject"), r.get("Predicate"), r.get("Target")) for r in seed["Relationships"]}

    remap = {}
    for sid in kit.shared:
        role = kit.role_of.get(sid)
        existing = bound_by_role.get(role) if role else None
        if existing is None:
            existing = by_name.get(kit.things[sid]["Name"])
        if existing and existing != sid:
            remap[sid] = existing

    def m(i):
        return remap.get(i, i)

    added_things = added_rels = 0
    for tid, t in kit.things.items():
        if tid in remap or (tid in thing_ids and (tid in kit.role_things or tid == ROLE_ARCHETYPE)):
            continue
        if tid in thing_ids:
            for i, existing in enumerate(seed["Things"]):
                if existing["Id"] == tid:
                    seed["Things"][i] = t
                    break
        else:
            seed["Things"].append(t)
            thing_ids.add(tid)
            added_things += 1

    for r in kit.rels.values():
        s, p, t = m(r["Subject"]), m(r["Predicate"]), m(r["Target"])
        key = (s, p, t)
        if key in rel_keys:
            continue
        seed["Relationships"].append({**r, "Subject": s, "Predicate": p, "Target": t})
        rel_keys.add(key)
        added_rels += 1

    return added_things, added_rels


def remove_from(kit, seed):
    """Strip a previously-merged playground back out, leaving the reconciled shared vocabulary in place
    along with the role Things, what they bind, and where each sits under the roots."""
    kept = kit.shared | kit.role_things
    drop_things = {tid for tid in kit.things if tid not in kept}
    drop_rels = {r["Id"] for r in kit.rels.values() if r["Subject"] not in kept or r["Target"] not in kept}
    before_t, before_r = len(seed.get("Things", [])), len(seed.get("Relationships", []))
    seed["Things"] = [t for t in seed.get("Things", []) if t["Id"] not in drop_things]
    seed["Relationships"] = [r for r in seed.get("Relationships", []) if r.get("Id") not in drop_rels]
    return before_t - len(seed["Things"]), before_r - len(seed["Relationships"])


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", metavar="FILE", help="write a standalone seed fragment")
    parser.add_argument("--into", metavar="SEED", help="merge the playground into an existing seed in place")
    parser.add_argument("--remove", action="store_true", help="with --into, strip the playground back out")
    parser.add_argument("--no-backup", action="store_true", help="with --into, do not write a .bak")
    parser.add_argument("--namespace", metavar="NAME",
                        help="derive per-model ids from NAME (pass the model/seed name) so the same DAGs "
                             "merged into different seeds get disjoint ids and coexist in one broker; omit "
                             "for the shared standalone identity. Use the same NAME with --remove.")
    args = parser.parse_args()

    set_namespace(args.namespace)
    kit = build()

    if not args.out and not args.into:
        parser.error("nothing to do: pass --out FILE and/or --into SEED")

    if args.out:
        dump_seed(as_fragment(kit), args.out)
        print(f"Wrote {args.out}: {len(kit.things)} things, {len(kit.rels)} relationships.")

    if args.into:
        seed = load_seed(args.into)
        if not args.no_backup:
            shutil.copyfile(args.into, args.into + ".bak")
        if args.remove:
            dt, dr = remove_from(kit, seed)
            print(f"Removed {dt} things, {dr} relationships from {seed.get('Name', args.into)}.")
        else:
            dt, dr = merge_into(kit, seed)
            print(f"Merged into {seed.get('Name', args.into)}: +{dt} things, +{dr} relationships.")
        dump_seed(seed, args.into)


if __name__ == "__main__":
    main()
