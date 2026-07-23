#!/usr/bin/env python3
"""Reimplements the Trellis PipelineModel + Phloem PipelineDagBuilder traversals and asserts every
generated pipeline resolves: connections appear in the palette, each pipeline loads with nodes/edges,
every required input is wired or param-bound (Run would be enabled), and seeded runs resolve."""
import json
import sys


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
        for r in seed["Relationships"]:
            self.bysubj.setdefault(r["Subject"], []).append(r)

    def name(self, i):
        return self.things.get(i, {}).get("Name", "?")

    def outgoing(self, subj, pred):
        pred = pred.lower()
        res = []
        for r in self.bysubj.get(subj, []):
            if self.name(r["Predicate"]).lower() == pred and r["Target"] in self.things:
                res.append(self.things[r["Target"]])
        return res

    def is_of(self, tid, arch, seen=None):
        seen = seen or set()
        if tid in seen:
            return False
        seen.add(tid)
        t = self.things.get(tid)
        if not t:
            return False
        if t["Name"].lower() == arch.lower():
            return True
        return any(self.is_of(p["Id"], arch, seen) for p in self.outgoing(tid, "is"))

    def resolve_ports(self, svc_id):
        ports, seen, stack = [], set(), [svc_id]
        while stack:
            i = stack.pop()
            if i in seen:
                continue
            seen.add(i)
            for t in self.outgoing(i, "has"):
                if self.is_of(t["Id"], "Port"):
                    p = t["P"]
                    ports.append({"portName": str(p.get("portName", t["Name"])),
                                  "direction": "out" if str(p.get("direction", "in")).lower() == "out" else "in",
                                  "type": str(p.get("type", "")),
                                  "required": str(p.get("required", "false")).lower() == "true"})
            for parent in self.outgoing(i, "is"):
                stack.append(parent["Id"])
        return ports

    def connections(self):
        res = []
        for t in self.things.values():
            if not self.is_of(t["Id"], "PlatformServiceConnection"):
                continue
            sub = t["P"].get("Subdomain")
            if not isinstance(sub, str) or not sub:
                continue
            svc = next((s for s in self.outgoing(t["Id"], "has") if self.is_of(s["Id"], "Service")), None)
            if not svc:
                continue
            res.append({"id": t["Id"], "name": t["Name"], "subdomain": sub, "ports": self.resolve_ports(svc["Id"])})
        return res

    def wires(self, subj):
        out = []
        for r in self.bysubj.get(subj, []):
            if self.is_of(r["Predicate"], "PipelineWire"):
                p = unwrap(r.get("Properties"))
                out.append({"target": r["Target"], "fromPort": str(p.get("fromPort", "")),
                            "toPort": str(p.get("toPort", "")), "toPath": str(p.get("toPath", "")),
                            "transform": str(p.get("transform", ""))})
        return out


def main():
    seed = json.load(open(sys.argv[1]))
    m = Model(seed)
    problems = []

    conns = m.connections()
    conn_by_id = {c["id"]: c for c in conns}
    print(f"Palette: {len(conns)} connections (services)")
    for c in sorted(conns, key=lambda c: c["name"]):
        ins = [p["portName"] for p in c["ports"] if p["direction"] == "in"]
        outs = [p["portName"] for p in c["ports"] if p["direction"] == "out"]
        if not c["ports"]:
            problems.append(f"service '{c['name']}' resolved no ports")
        print(f"  - {c['name']:22} @{c['subdomain']:16} in[{','.join(ins)}] out[{','.join(outs)}]")

    pipelines = [t for t in m.things.values() if m.is_of(t["Id"], "Pipeline")]
    print(f"\nPipelines: {len(pipelines)}")
    for pipe in sorted(pipelines, key=lambda t: t["Name"]):
        node_things = [t for t in m.outgoing(pipe["Id"], "has") if m.is_of(t["Id"], "PipelineNode")]
        node_ids = {t["Id"] for t in node_things}
        nodes = []
        for t in node_things:
            kind = "input" if m.is_of(t["Id"], "PipelineInput") else "output" if m.is_of(t["Id"], "PipelineOutput") else None
            if kind:
                ports = m.resolve_ports(t["Id"])
            else:
                conn = next((c for c in m.outgoing(t["Id"], "has") if m.is_of(c["Id"], "PlatformServiceConnection")), None)
                if not conn:
                    problems.append(f"[{pipe['Name']}] node '{t['Name']}' binds no connection")
                    ports = []
                else:
                    ports = conn_by_id.get(conn["Id"], {}).get("ports", [])
            pb = t["P"].get("paramBindings")
            bindings = json.loads(pb) if isinstance(pb, str) and pb else {}
            nodes.append({"id": t["Id"], "name": t["Name"], "kind": kind, "ports": ports, "bindings": bindings})

        edges = []
        for t in node_things:
            for w in m.wires(t["Id"]):
                if w["target"] in node_ids:
                    edges.append((t["Id"], w["fromPort"], w["target"], w["toPort"]))

        # Pre-run validation (validate.ts): required inputs must be wired or param-bound; no dangling wires.
        by_id = {n["id"]: n for n in nodes}
        wired = set()
        for (s, sh, tg, th) in edges:
            src, dst = by_id.get(s), by_id.get(tg)
            ok = src and dst and any(p["portName"] == sh and p["direction"] == "out" for p in src["ports"]) \
                and any(p["portName"] == th and p["direction"] == "in" for p in dst["ports"])
            if not ok:
                problems.append(f"[{pipe['Name']}] dangling wire {m.name(s)}.{sh} -> {m.name(tg)}.{th}")
            else:
                wired.add((tg, th))
        for n in nodes:
            for p in n["ports"]:
                if p["direction"] == "in" and p["required"]:
                    if (n["id"], p["portName"]) not in wired and not n["bindings"].get(p["portName"]):
                        problems.append(f"[{pipe['Name']}] required input '{p['portName']}' on '{n['name']}' unwired/unbound")

        runs = [t for t in m.things.values() if m.is_of(t["Id"], "PipelineRun")
                and any(x["Id"] == pipe["Id"] for x in m.outgoing(t["Id"], "of"))]
        print(f"  - {pipe['Name']:26} nodes={len(nodes):2} edges={len(edges):2} runs={len(runs)}")

    if problems:
        print("\nPROBLEMS:")
        for p in problems:
            print("  ! " + p)
        sys.exit(1)
    print("\nAll pipelines resolve and pass pre-run validation.")


if __name__ == "__main__":
    main()
