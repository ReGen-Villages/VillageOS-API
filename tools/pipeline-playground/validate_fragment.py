#!/usr/bin/env python3
"""Reimplements the Trellis PipelineModel + Phloem PipelineDagBuilder traversals and asserts every
generated pipeline resolves: every role is bound to a Thing, connections appear in the palette, each
pipeline loads with nodes/edges, every required input is wired or param-bound (Run would be enabled), and
seeded runs resolve. Every read here goes through the role Thing that binds a Thing, never through a name."""
import json
import sys

# The role identifiers, spelled out here rather than derived or imported from the generator on purpose:
# this file is the check that what the generator wrote is what the editor and the orchestrator read, and
# an identifier it shared with the thing it checks would agree with itself however wrong it was.
PIPELINE = "f8bc9986-6eef-54d9-9940-4aca2497109b"
PIPELINE_NODE = "f273f058-e3d6-5a20-aa37-34638ce64735"
PIPELINE_INPUT = "634f9ae3-2cca-5dd9-84aa-fec4dff07a6d"
PIPELINE_OUTPUT = "880d2158-25a8-5b2b-8b6a-923f21ca153d"
PORT = "318d6995-e07b-5c83-acb5-86cd935ba4c6"
SERVICE = "768bb2e8-e738-5345-9126-c23ad1513197"
CONNECTION = "d5a3c29f-67c1-55a5-b0e4-9241378bd08d"
PIPELINE_WIRE = "adfe0071-bbaf-5e7f-abc3-255c08d8735a"
PIPELINE_RUN = "b682b68f-0101-5ab4-a7a9-13c3e43df873"
EXTERNAL_SYSTEM = "03164f47-30c0-5984-9215-a81317000ec5"
MESSAGE_KIND = "0b2082d2-eec5-5eec-85dd-66431d776731"
CADENCE = "f3883eaf-4e52-5c0c-887b-1bc696578812"
SENDS = "ab3334f4-cb8e-5513-94b4-43debdb73acd"
TOLD = "08104fb0-e74f-511a-9598-62c20da9083c"
TOLD_THROUGH = "d94c1844-76e6-56fa-ae38-d10405d52826"
ARRIVES_AT = "79f0afcb-8c66-53dd-8081-43af167fc07a"
STANDS_FOR = "4277647a-93f8-5860-8717-da2a12673dbf"
CHOICE = "043b23a9-d2e8-5a8a-b81c-1a56bf0f525a"
INPUT_CONNECTOR = "1f13d260-b2cc-5757-a475-7eb9b7d2323f"
OUTPUT_CONNECTOR = "7788a338-5eb6-5733-a208-0cf90b4269dd"

# The roles a DAG is resolved from. Bound to nothing, every question below answers "no" and a model
# full of pipelines reads as empty — which is why Phloem refuses such a model outright.
DAG_ROLES = {PIPELINE: "pipeline", PIPELINE_NODE: "pipelineNode", PIPELINE_INPUT: "pipelineInput",
             PIPELINE_OUTPUT: "pipelineOutput", PORT: "port", SERVICE: "service", CONNECTION: "connection",
             PIPELINE_WIRE: "pipelineWire", INPUT_CONNECTOR: "inputConnector", OUTPUT_CONNECTOR: "outputConnector"}


def unwrap(props):
    out = {}
    for name, v in (props or {}).items():
        out[name] = v["value"] if isinstance(v, dict) and "value" in v else v
    return out


class Model:
    def __init__(self, seed):
        self.things = {t["Id"]: {"Id": t["Id"], "Name": t["Name"], "P": unwrap(t.get("Properties"))}
                       for t in seed["Things"]}
        self.bysubj = {}
        self.bound = {}
        for r in seed["Relationships"]:
            self.bysubj.setdefault(r["Subject"], []).append(r)
            if self.name(r["Predicate"]).lower() == "binds" and r["Target"] in self.things:
                self.bound.setdefault(r["Subject"], []).append(r["Target"])

    def name(self, i):
        return self.things.get(i, {}).get("Name", "?")

    def outgoing(self, subj, pred):
        pred = pred.lower()
        res = []
        for r in self.bysubj.get(subj, []):
            if self.name(r["Predicate"]).lower() == pred and r["Target"] in self.things:
                res.append(self.things[r["Target"]])
        return res

    def bound_to(self, role):
        """The one Thing this model binds to a role, or None when it binds none or two."""
        played = self.bound.get(role, [])
        return played[0] if len(played) == 1 else None

    def is_of(self, tid, role, seen=None):
        """Does this Thing `is`, directly or through its `is`-chain, what the role binds? The walk starts
        above the Thing, so an archetype never plays its own role."""
        archetype = self.bound_to(role)
        if archetype is None:
            return False
        seen = seen if seen is not None else {tid}
        for parent in self.outgoing(tid, "is"):
            if parent["Id"] in seen:
                continue
            seen.add(parent["Id"])
            if parent["Id"] == archetype or self.is_of(parent["Id"], role, seen):
                return True
        return False

    def resolve_ports(self, owner_id):
        """The Port Things an owner `has`, up its `is` chain: a service's, or the ones a node holds itself."""
        ports, seen, stack = [], set(), [owner_id]
        while stack:
            i = stack.pop()
            if i in seen:
                continue
            seen.add(i)
            for t in self.outgoing(i, "has"):
                if self.is_of(t["Id"], PORT):
                    p = t["P"]
                    ports.append({"id": t["Id"],
                                  "portName": str(p.get("portName", t["Name"])),
                                  "direction": "out" if str(p.get("direction", "in")).lower() == "out" else "in",
                                  "type": str(p.get("type", "")),
                                  "required": str(p.get("required", "false")).lower() == "true"})
            for parent in self.outgoing(i, "is"):
                stack.append(parent["Id"])
        return ports

    def connections(self):
        res = []
        for t in self.things.values():
            if not self.is_of(t["Id"], CONNECTION):
                continue
            sub = t["P"].get("Subdomain")
            if not isinstance(sub, str) or not sub:
                continue
            svc = next((s for s in self.outgoing(t["Id"], "has") if self.is_of(s["Id"], SERVICE)), None)
            if not svc:
                continue
            res.append({"id": t["Id"], "name": t["Name"], "subdomain": sub, "ports": self.resolve_ports(svc["Id"])})
        return res

    def reached_along(self, subj, role):
        """Targets reached from a Thing along the predicate the role binds — how the console reads what a
        system sends, where a kind arrives, and what a boundary node stands for."""
        predicate = self.bound_to(role)
        return [self.things[r["Target"]] for r in self.bysubj.get(subj, [])
                if r["Predicate"] == predicate and r["Target"] in self.things]

    def wires(self, subj):
        out = []
        for held in self.outgoing(subj, "has"):
            if not self.is_of(held["Id"], PIPELINE_WIRE):
                continue
            target = next((r["Target"] for r in self.bysubj.get(held["Id"], []) if self.is_of(r["Target"], PIPELINE_NODE)), None)
            if target:
                out.append({"name": held["Name"], "target": target,
                            "out": [p["Id"] for p in self.reached_along(held["Id"], INPUT_CONNECTOR)],
                            "in": [p["Id"] for p in self.reached_along(held["Id"], OUTPUT_CONNECTOR)]})
        return out

    def relationship_wires(self, subj):
        return [r["Target"] for r in self.bysubj.get(subj, []) if self.is_of(r["Predicate"], PIPELINE_WIRE)]


def main():
    seed = json.load(open(sys.argv[1]))
    m = Model(seed)
    problems = []

    unbound = [word for role, word in DAG_ROLES.items() if m.bound_to(role) is None]
    if unbound:
        problems.append("no Thing is bound to the roles: " + ", ".join(unbound))

    conns = m.connections()
    conn_by_id = {c["id"]: c for c in conns}
    print(f"Palette: {len(conns)} connections (services)")
    for c in sorted(conns, key=lambda c: c["name"]):
        ins = [p["portName"] for p in c["ports"] if p["direction"] == "in"]
        outs = [p["portName"] for p in c["ports"] if p["direction"] == "out"]
        if not c["ports"]:
            problems.append(f"service '{c['name']}' resolved no ports")
        print(f"  - {c['name']:22} @{c['subdomain']:16} in[{','.join(ins)}] out[{','.join(outs)}]")

    pipelines = [t for t in m.things.values() if m.is_of(t["Id"], PIPELINE)]
    print(f"\nPipelines: {len(pipelines)}")
    for pipe in sorted(pipelines, key=lambda t: t["Name"]):
        node_things = [t for t in m.outgoing(pipe["Id"], "has") if m.is_of(t["Id"], PIPELINE_NODE)]
        node_ids = {t["Id"] for t in node_things}
        nodes = []
        for t in node_things:
            kind = "input" if m.is_of(t["Id"], PIPELINE_INPUT) else "output" if m.is_of(t["Id"], PIPELINE_OUTPUT) else None
            if kind:
                ports = m.resolve_ports(t["Id"])
            else:
                conn = next((c for c in m.outgoing(t["Id"], "has") if m.is_of(c["Id"], CONNECTION)), None)
                if not conn:
                    problems.append(f"[{pipe['Name']}] node '{t['Name']}' binds no connection")
                    ports = []
                else:
                    ports = m.resolve_ports(t["Id"]) + conn_by_id.get(conn["Id"], {}).get("ports", [])
                # A Thing the node chose must be offered by one of its in ports along the same predicate, or
                # the orchestrator refuses the run.
                for chosen in m.reached_along(t["Id"], CHOICE):
                    if not any(p["direction"] == "in" and any(o["Id"] == chosen["Id"] for o in m.reached_along(p["id"], CHOICE))
                               for p in ports):
                        problems.append(f"[{pipe['Name']}] node '{t['Name']}' chose '{chosen['Name']}', which no in port of it offers")
            pb = t["P"].get("parameterBindings")
            bindings = json.loads(pb) if isinstance(pb, str) and pb else {}
            nodes.append({"id": t["Id"], "name": t["Name"], "kind": kind, "ports": ports, "bindings": bindings})

        # Pre-run validation (PipelineDagBuilder and validate.ts): each wire relates to one out port of its node and
        # one in port of its target; required inputs must be wired or param-bound.
        by_id = {n["id"]: n for n in nodes}
        wired = set()
        edges = 0
        for t in node_things:
            for target in m.relationship_wires(t["Id"]):
                if target in node_ids:
                    problems.append(f"[{pipe['Name']}] the wire from '{t['Name']}' to '{m.name(target)}' is drawn as a relationship between the two nodes")
            for w in m.wires(t["Id"]):
                if w["target"] not in node_ids:
                    continue
                edges += 1
                named = f"[{pipe['Name']}] the wire from '{t['Name']}' to '{m.name(w['target'])}'"
                ends = [(w["out"], by_id[t["Id"]], "out", "takes its value from", "inputConnector"),
                        (w["in"], by_id[w["target"]], "in", "delivers its value to", "outputConnector")]
                resolved = []
                for connected, node, direction, reaching, word in ends:
                    if len(connected) != 1:
                        problems.append(f"{named} relates to {len(connected)} ports along {word}")
                        continue
                    port = next((p for p in node["ports"] if p["id"] == connected[0] and p["direction"] == direction), None)
                    if port is None:
                        problems.append(f"{named} {reaching} '{m.name(connected[0])}', which is not an {direction} port of '{node['name']}'")
                        continue
                    resolved.append(port)
                if len(resolved) == 2:
                    wired.add((w["target"], resolved[1]["portName"]))
        for n in nodes:
            for p in n["ports"]:
                if p["direction"] == "in" and p["required"]:
                    if (n["id"], p["portName"]) not in wired and not n["bindings"].get(p["portName"]):
                        problems.append(f"[{pipe['Name']}] required input '{p['portName']}' on '{n['name']}' unwired/unbound")

        # What each boundary node stands for (model.ts endInformation): a start may stand for a kind some
        # system sends, a system that sends, a door, a cadence or a pipeline; an end for a kind some system is told, a
        # system naming the connection it is told through (Phloem refuses the run otherwise), or a pipeline.
        # Standing for nothing is fine at either end.
        stands = []
        for t in node_things:
            for stood in m.reached_along(t["Id"], STANDS_FOR):
                is_start = m.is_of(t["Id"], PIPELINE_INPUT)
                sid = stood["Id"]
                if m.is_of(sid, MESSAGE_KIND):
                    may = any(sid in [k["Id"] for k in m.reached_along(s["Id"], SENDS if is_start else TOLD)]
                              for s in m.things.values() if m.is_of(s["Id"], EXTERNAL_SYSTEM))
                elif m.is_of(sid, EXTERNAL_SYSTEM):
                    may = bool(m.reached_along(sid, SENDS)) if is_start else any(
                        m.is_of(c["Id"], CONNECTION) for c in m.reached_along(sid, TOLD_THROUGH))
                elif m.is_of(sid, PIPELINE):
                    may = True
                elif m.is_of(sid, CONNECTION) or m.is_of(sid, CADENCE):
                    may = is_start
                else:
                    may = False
                if not may:
                    end = "starts" if is_start else "ends"
                    problems.append(f"[{pipe['Name']}] '{t['Name']}' {end} at '{stood['Name']}', which a run cannot {'come from' if is_start else 'leave behind'}")
                stands.append(f"{t['Name']} -> {stood['Name']}")

        runs = [t for t in m.things.values() if m.is_of(t["Id"], PIPELINE_RUN)
                and any(x["Id"] == pipe["Id"] for x in m.outgoing(t["Id"], "of"))]
        print(f"  - {pipe['Name']:26} nodes={len(nodes):2} edges={edges:2} runs={len(runs)}"
              + (f"  stands for: {'; '.join(stands)}" if stands else ""))

    systems = [t for t in m.things.values() if m.is_of(t["Id"], EXTERNAL_SYSTEM)]
    print(f"\nExternal systems: {len(systems)}")
    for system in sorted(systems, key=lambda t: t["Name"]):
        sent = m.reached_along(system["Id"], SENDS)
        told = m.reached_along(system["Id"], TOLD)
        for kind in sent + told:
            if not m.is_of(kind["Id"], MESSAGE_KIND):
                problems.append(f"system '{system['Name']}' reaches '{kind['Name']}', which is not a kind of message")
        for kind in sent:
            doors = [d for d in m.reached_along(kind["Id"], ARRIVES_AT) if d["Id"] in conn_by_id]
            if not doors:
                problems.append(f"'{kind['Name']}' is sent by '{system['Name']}' and arrives at no door")
        print(f"  - {system['Name']:22} sends[{','.join(k['Name'] for k in sent)}] told[{','.join(k['Name'] for k in told)}]")

    if problems:
        print("\nPROBLEMS:")
        for p in problems:
            print("  ! " + p)
        sys.exit(1)
    print("\nAll pipelines resolve and pass pre-run validation.")


if __name__ == "__main__":
    main()
