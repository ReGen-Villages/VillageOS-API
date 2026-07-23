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
same Things instead of forking duplicates. When merging, the built-in `is`/`has`/`of` predicates and the
pipeline archetypes are reconciled by name against the target — an existing `is` is reused, never doubled.
"""

import argparse
import json
import shutil
import sys
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from catalog import SERVICES, PIPELINES

# Shared with tools/simulator so ids are consistent across the ecosystem. With no --namespace this root is
# used directly, keeping the standalone fragment's ids stable. --namespace NAME derives a per-model root
# (uuid5 of NAME under this root), so the same DAGs merged into two different model seeds get disjoint ids
# and can coexist in one broker rather than colliding.
_ROOT_NAMESPACE = uuid.UUID("6f9b1e2c-4a7d-5b8e-9c0f-1d2e3a4b5c6d")
_id_namespace = _ROOT_NAMESPACE
_TAG = "pipeline-playground"


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
    idempotent. `shared` marks the generic vocabulary (is/has/of + the pipeline archetypes) that is
    reconciled against a target model by name at merge time rather than blindly duplicated."""

    def __init__(self):
        self.things = {}          # id -> Thing dict
        self.rels = {}            # dedup key -> Relationship dict
        self.shared = set()       # ids of reconcile-by-name vocabulary Things
        self._name = {}           # id -> Name (for readable relationship labels)

    def thing(self, tid, name, properties=None, shared=False):
        if tid not in self.things:
            self.things[tid] = {"Id": tid, "Name": name, "Properties": properties or {}}
            self._name[tid] = name
            if shared:
                self.shared.add(tid)
        return tid

    def rel(self, subject, predicate, target, properties=None):
        key = (subject, predicate, target,
               _prop(properties, "fromPort"), _prop(properties, "toPort"), _prop(properties, "toPath"))
        if key not in self.rels:
            name = f"{self._name.get(subject, '?')} {self._name.get(predicate, '?')} {self._name.get(target, '?')}"
            self.rels[key] = {
                "Id": stable_id(_TAG, "rel", *(str(k) for k in key)),
                "Name": name,
                "Subject": subject, "Predicate": predicate, "Target": target,
                "Properties": properties or {},
            }
        return self.rels[key]

    # --- generic vocabulary (reconciled by name) ------------------------------------------------
    def predicate(self, name):
        return self.thing(stable_id(_TAG, "predicate", name), name, shared=True)

    def archetype(self, name):
        return self.thing(stable_id(_TAG, "archetype", name), name, shared=True)


def _prop(properties, name):
    """A property's raw value out of the typed envelope (for building a dedup key); '' when absent."""
    entry = (properties or {}).get(name)
    return entry["value"] if isinstance(entry, dict) else ""


def build():
    k = Kit()

    is_ = k.predicate("is")
    has = k.predicate("has")
    of = k.predicate("of")
    feeds = k.predicate("feeds")

    Pipeline = k.archetype("Pipeline")
    PipelineNode = k.archetype("PipelineNode")
    PipelineInput = k.archetype("PipelineInput")
    PipelineOutput = k.archetype("PipelineOutput")
    Port = k.archetype("Port")
    Service = k.archetype("Service")
    Connection = k.archetype("PlatformServiceConnection")
    PipelineWire = k.archetype("PipelineWire")
    PipelineRun = k.archetype("PipelineRun")
    NodeRun = k.archetype("NodeRun")

    k.rel(feeds, is_, PipelineWire)          # wires are the `feeds` predicate, identified by this archetype
    k.rel(PipelineInput, is_, PipelineNode)  # boundary nodes are pipeline nodes too
    k.rel(PipelineOutput, is_, PipelineNode)

    def make_port(owner_id, owner_key, name, direction, type, required, collection):
        pid = stable_id(_TAG, "port", owner_key, name, direction)
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
    # entry, carrying the dispatch Subdomain).
    connection_by_key = {}
    for svc in SERVICES:
        svc_id = k.thing(stable_id(_TAG, "service", svc["key"]), f"{svc['label']} service")
        k.rel(svc_id, is_, Service)
        for (name, direction, type, required, collection) in svc["ports"]:
            make_port(svc_id, svc["key"], name, direction, type, required, collection)

        conn_id = k.thing(stable_id(_TAG, "connection", svc["key"]), svc["label"],
                          {"Subdomain": typed(svc["subdomain"], "vos.String")})
        k.rel(conn_id, is_, Connection)
        k.rel(conn_id, has, svc_id)
        connection_by_key[svc["key"]] = conn_id

    for pipe in PIPELINES:
        pipe_id = k.thing(stable_id(_TAG, "pipeline", pipe["name"]), pipe["name"])
        k.rel(pipe_id, is_, Pipeline)

        node_id = {}
        for node in pipe["nodes"]:
            nid = stable_id(_TAG, "node", pipe["name"], node["key"])
            node_id[node["key"]] = nid
            x = _ORIGIN + node["at"]["col"] * _COL_WIDTH
            y = _ORIGIN + node["at"]["row"] * _ROW_HEIGHT
            props = {"x": typed(float(x), "vos.Double"), "y": typed(float(y), "vos.Double")}
            if node.get("params"):
                props["paramBindings"] = typed(json.dumps(node["params"]), "vos.String")
            if node.get("onItemError"):
                props["onItemError"] = typed(node["onItemError"], "vos.String")

            if "service" in node:
                label = node.get("label", next(s["label"] for s in SERVICES if s["key"] == node["service"]))
                k.thing(nid, label, props)
                k.rel(nid, is_, PipelineNode)
                k.rel(nid, has, connection_by_key[node["service"]])
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

            k.rel(pipe_id, has, nid)

        for wire in pipe["wires"]:
            props = {
                "fromPort": typed(wire["fromPort"], "vos.String"),
                "toPort": typed(wire["toPort"], "vos.String"),
            }
            for optional in ("fromPath", "toPath", "transform"):
                if wire.get(optional):
                    props[optional] = typed(wire[optional], "vos.String")
            k.rel(node_id[wire["from"]], feeds, node_id[wire["to"]], props)

        # Seeded run history (#5646/#5635): a PipelineRun -of-> pipeline, each carrying NodeRuns so the History
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


def merge_into(kit, seed):
    """Merge the kit into a loaded seed in place, reconciling the shared vocabulary by name so an existing
    `is`/`has`/archetype is reused, not duplicated."""
    seed.setdefault("Things", [])
    seed.setdefault("Relationships", [])
    by_name = {}
    for t in seed["Things"]:
        by_name.setdefault(t["Name"], t["Id"])
    thing_ids = {t["Id"] for t in seed["Things"]}
    rel_keys = {(r.get("Subject"), r.get("Predicate"), r.get("Target"),
                 _rel_prop(r, "fromPort"), _rel_prop(r, "toPort"), _rel_prop(r, "toPath"))
                for r in seed["Relationships"]}

    remap = {}
    for sid in kit.shared:
        existing = by_name.get(kit.things[sid]["Name"])
        if existing and existing != sid:
            remap[sid] = existing

    def m(i):
        return remap.get(i, i)

    added_things = added_rels = 0
    for tid, t in kit.things.items():
        if tid in remap:
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
        key = (s, p, t, _rel_prop(r, "fromPort"), _rel_prop(r, "toPort"), _rel_prop(r, "toPath"))
        if key in rel_keys:
            continue
        seed["Relationships"].append({**r, "Subject": s, "Predicate": p, "Target": t})
        rel_keys.add(key)
        added_rels += 1

    return added_things, added_rels


def remove_from(kit, seed):
    """Strip a previously-merged playground back out (leaves the reconciled shared vocabulary in place)."""
    drop_things = {tid for tid in kit.things if tid not in kit.shared}
    drop_rels = {r["Id"] for r in kit.rels.values()}
    before_t, before_r = len(seed.get("Things", [])), len(seed.get("Relationships", []))
    seed["Things"] = [t for t in seed.get("Things", []) if t["Id"] not in drop_things]
    seed["Relationships"] = [r for r in seed.get("Relationships", []) if r.get("Id") not in drop_rels]
    return before_t - len(seed["Things"]), before_r - len(seed["Relationships"])


def _rel_prop(rel, name):
    entry = rel.get("Properties", {}).get(name)
    return entry["value"] if isinstance(entry, dict) else ""


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
