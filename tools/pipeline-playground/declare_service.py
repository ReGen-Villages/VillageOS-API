#!/usr/bin/env python3
"""Declare one service in a seed from its manifest, with a published pipeline that runs it.

A pipeline can use a service only when the model declares it: the service, its connection, its ports and
the vocabulary the orchestrator refuses to work without. This writes all of that into a seed in place from
what the service says about itself at `GET /manifest`, plus what only the deployment knows: the subdomain
it is reached at, its port and its program path.

    # from a saved manifest
    python3 declare_service.py --manifest quadrat.manifest.json --subdomain quadrat --port 5110 \\
        --program /srv/quadrat/app.py --into /path/to/Some.seed.json

    # from a running service
    python3 declare_service.py --manifest http://localhost:5110/manifest --subdomain quadrat --port 5110 \\
        --program /srv/quadrat/app.py --into /path/to/Some.seed.json

    # take it out again (the same --manifest, --subdomain and --namespace as when it went in)
    python3 declare_service.py --manifest quadrat.manifest.json --subdomain quadrat --into Some.seed.json --remove

A seed that already declares the service keeps its own Things: the connection is found by its subdomain,
the service as what the connection holds, and each port by its name and direction; what the manifest lists
and the seed lacks is added. Running it a second time changes nothing.
"""

import argparse
import json
import shutil
import sys
import urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import generate as G
from generate import typed

TAG = "declared-service"

# What a port in a manifest is read as; `type` empty is a wildcard, as the catalog's ports read it.
PORT_DEFAULTS = {"type": "", "required": False, "collection": False}


class ManifestError(Exception):
    """The manifest does not say what the model needs of the service."""


def read_manifest(source):
    if source.startswith(("http://", "https://")):
        with urllib.request.urlopen(source) as answer:
            text = answer.read().decode("utf-8")
    else:
        text = Path(source).read_text(encoding="utf-8")
    return parse_manifest(json.loads(text))


def parse_manifest(document):
    """A manifest is the object a service answers at GET /manifest, or the bare list of ports an older
    service answers. Each port names itself and its direction; `rehearses` and `settings` are read when
    present."""
    if isinstance(document, list):
        document = {"ports": document}
    if not isinstance(document, dict) or not isinstance(document.get("ports"), list):
        raise ManifestError("a manifest is an object with a `ports` list, or a bare list of ports")
    ports = []
    for port in document["ports"]:
        if not isinstance(port, dict) or not port.get("portName") or port.get("direction") not in ("in", "out"):
            raise ManifestError(f"every port names itself and a direction of `in` or `out`; got {json.dumps(port)}")
        ports.append({**PORT_DEFAULTS, **{key: port[key] for key in ("portName", "direction", *PORT_DEFAULTS) if key in port}})
    settings = []
    for setting in document.get("settings", []):
        if not isinstance(setting, dict) or not setting.get("name"):
            raise ManifestError(f"every setting has a `name`; got {json.dumps(setting)}")
        settings.append({"name": setting["name"], "required": setting.get("required") is True,
                         "values": list(setting.get("values", [])), "default": setting.get("default")})
    return {"name": document.get("service"), "rehearses": document.get("rehearses") is True,
            "ports": ports, "settings": settings}


def setting_value(setting):
    """What the node carries for a setting until someone sets it: the manifest's default, the first allowed
    value when one is required, and otherwise an empty value, declared because a property write on a Thing
    updates and never creates."""
    if setting["default"] is not None:
        return setting["default"]
    if setting["required"] and setting["values"]:
        return setting["values"][0]
    return ""


def typed_setting(value):
    if isinstance(value, bool):
        return typed(value, "vos.Boolean")
    if isinstance(value, int):
        return typed(value, "vos.LongInteger")
    if isinstance(value, float):
        return typed(value, "vos.Double")
    return typed(str(value), "vos.String")


def build(manifest, subdomain, name, port=None, program=None):
    """The kit declaring the service, its connection, its ports and a published pipeline running it, over the
    vocabulary the orchestrator requires. Port and program are left out when only the ids are needed."""
    k = G.Kit()
    v = G.declare_dag_vocabulary(k)
    d = {}

    service_properties = {"rehearses": typed(manifest["rehearses"], "vos.Boolean")}
    if program is not None:
        service_properties["ExecutablePath"] = typed(program, "vos.String")
    if port is not None:
        service_properties["ServicePort"] = typed(port, "vos.LongInteger")
    d["service"] = k.thing(G.stable_id(TAG, "service", subdomain), f"{name} service", service_properties)
    k.rel(d["service"], v.is_, v.Service)
    port_ids = {}
    for p in manifest["ports"]:
        G.declare_port(k, v, port_ids, d["service"], subdomain, p["portName"], p["direction"], p["type"],
                       p["required"], p["collection"], tag=TAG)
    d["ports"] = {(p["portName"], p["direction"]): port_ids[(d["service"], p["portName"], p["direction"])]
                  for p in manifest["ports"]}

    d["connection"] = k.thing(G.stable_id(TAG, "connection", subdomain), name, {"Subdomain": typed(subdomain, "vos.String")})
    k.rel(d["connection"], v.is_, v.Connection)
    k.rel(d["connection"], v.has, d["service"])
    k.rel(d["connection"], v.triggered_by, v.http)

    d["pipeline"] = k.thing(G.stable_id(TAG, "pipeline", subdomain), f"{name} pipeline", {"published": typed(True, "vos.Boolean")})
    k.rel(d["pipeline"], v.is_, v.Pipeline)

    required_inputs = [p for p in manifest["ports"] if p["direction"] == "in" and p["required"]]
    outputs = [p for p in manifest["ports"] if p["direction"] == "out"]
    column = 0
    nodes = []
    if required_inputs:
        input_node = k.thing(G.stable_id(TAG, "node", subdomain, "input"), "Input", G.canvas_position(column))
        k.rel(input_node, v.is_, v.PipelineNode)
        k.rel(input_node, v.is_, v.PipelineInput)
        nodes.append(input_node)
        column += 1
    settings = {s["name"]: typed_setting(setting_value(s)) for s in manifest["settings"]}
    service_node = k.thing(G.stable_id(TAG, "node", subdomain, "service"), name, {**G.canvas_position(column), **settings})
    k.rel(service_node, v.is_, v.PipelineNode)
    k.rel(service_node, v.has, d["connection"])
    nodes.append(service_node)
    output_node = k.thing(G.stable_id(TAG, "node", subdomain, "output"), "Output", G.canvas_position(column + 1))
    k.rel(output_node, v.is_, v.PipelineNode)
    k.rel(output_node, v.is_, v.PipelineOutput)
    nodes.append(output_node)
    for node in nodes:
        k.rel(d["pipeline"], v.has, node)

    def wire(source, out_port, target, in_port, taken):
        out_name, in_name = k.things[out_port]["Properties"]["portName"]["value"], k.things[in_port]["Properties"]["portName"]["value"]
        wid = G.stable_id(TAG, "wire", subdomain, k.things[source]["Name"], out_name, k.things[target]["Name"], in_name)
        k.thing(wid, f"{k.things[source]['Name']}.{out_name} to {k.things[target]['Name']}.{in_name}",
                {field: typed("", "vos.String") for field in ("fromPath", "toPath", "transform")})
        k.rel(wid, v.is_, v.PipelineWire)
        k.rel(source, v.has, wid)
        k.rel(wid, v.carries, target)
        k.rel(wid, v.input_connector, out_port)
        k.rel(wid, v.output_connector, in_port)
        k.rel(wid, v.taken_on, v.results[taken])

    for p in required_inputs:
        filled_from = G.declare_port(k, v, port_ids, input_node, f"{subdomain}.input", p["portName"], "out", p["type"], False, False, tag=TAG)
        wire(input_node, filled_from, service_node, d["ports"][(p["portName"], "in")], "success")
    for p in outputs:
        delivered_to = G.declare_port(k, v, port_ids, output_node, f"{subdomain}.output", p["portName"], "in", p["type"], False, False, tag=TAG)
        wire(service_node, d["ports"][(p["portName"], "out")], output_node, delivered_to, "success")
    error_to = G.declare_port(k, v, port_ids, output_node, f"{subdomain}.output", "error", "in", "string", False, False, tag=TAG)
    wire(service_node, v.error_port, output_node, error_to, "fail")

    G.place_under_roots(k, v)
    return k, d


def property_value(thing, name):
    entry = (thing.get("Properties") or {}).get(name)
    return entry.get("value") if isinstance(entry, dict) else entry


def already_declared(seed, declared, subdomain):
    """What the seed already holds of the service, by the kit's id: the connection carrying the subdomain,
    the service it holds, and each port the service or a kind above it holds, by port name and direction.
    The orchestrator finds them the same way, so a seed that names them its own way keeps its names."""
    things = {t["Id"]: t for t in seed.get("Things", [])}
    connection = next((t["Id"] for t in things.values()
                       if str(property_value(t, "Subdomain") or "").lower() == subdomain.lower()), None)
    if connection is None:
        return {}
    found = {declared["connection"]: connection}
    predicate_names = {t["Id"]: t["Name"] for t in things.values()}
    held_by = {}
    is_parents = {}
    service_archetype = None
    for r in seed.get("Relationships", []):
        word = predicate_names.get(r.get("Predicate"))
        if word == "has":
            held_by.setdefault(r["Subject"], []).append(r["Target"])
        elif word == "is":
            is_parents.setdefault(r["Subject"], []).append(r["Target"])
        elif word == "binds" and r["Subject"] == G.role_id("service") and service_archetype is None:
            service_archetype = r["Target"]

    def is_of(tid, archetype):
        seen, stack = set(), [tid]
        while stack:
            current = stack.pop()
            if current in seen:
                continue
            seen.add(current)
            if archetype in is_parents.get(current, []):
                return True
            stack.extend(is_parents.get(current, []))
        return False

    # What the connection holds that is a service, as the orchestrator reads it; a seed binding no service
    # role has not said what a service is, so whatever the connection holds first stands.
    held = [held for held in held_by.get(connection, []) if held in things]
    service = next((h for h in held if service_archetype is None or is_of(h, service_archetype)), None)
    if service is None:
        return found
    found[declared["service"]] = service
    seen, stack = set(), [service]
    while stack:
        owner = stack.pop()
        if owner in seen:
            continue
        seen.add(owner)
        for held in held_by.get(owner, []):
            port = things.get(held)
            if port is None or property_value(port, "portName") is None:
                continue
            key = (str(property_value(port, "portName")), str(property_value(port, "direction") or "in").lower())
            if key in declared["ports"] and declared["ports"][key] not in found:
                found[declared["ports"][key]] = held
        stack.extend(is_parents.get(owner, []))
    return found


def describe(seed, kit, declared, found):
    """One line per Thing of the service the seed already held, and one per Thing it lacked."""
    names = {t["Id"]: t["Name"] for t in seed.get("Things", [])}
    lines = []
    for what, kit_id in (("connection", declared["connection"]), ("service", declared["service"])):
        if kit_id in found:
            lines.append(f"found {what} '{names[found[kit_id]]}'")
        else:
            lines.append(f"added {what} '{kit.things[kit_id]['Name']}'")
    for (port_name, direction), kit_id in declared["ports"].items():
        lines.append(f"{'found' if kit_id in found else 'added'} {direction} port '{port_name}'")
    lines.append(f"added pipeline '{kit.things[declared['pipeline']]['Name']}'")
    return lines


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--manifest", required=True, metavar="FILE_OR_URL",
                        help="the service's manifest: a saved copy, or the GET /manifest address of a running service")
    parser.add_argument("--subdomain", required=True, help="the word the platform forwards a call to the service under")
    parser.add_argument("--into", required=True, metavar="SEED", help="the seed to edit in place")
    parser.add_argument("--port", type=int, metavar="NUMBER", help="the port the platform starts the service on")
    parser.add_argument("--program", metavar="PATH", help="the program the platform starts the service from")
    parser.add_argument("--name", help="what to call the service; the manifest's own name, then the subdomain, when omitted")
    parser.add_argument("--remove", action="store_true", help="take the service and its pipeline back out")
    parser.add_argument("--no-backup", action="store_true", help="do not write a .bak beside the seed")
    parser.add_argument("--namespace", metavar="NAME",
                        help="derive per-model ids from NAME, as generate.py does; use the same NAME with --remove")
    args = parser.parse_args()
    if not args.remove and (args.port is None or args.program is None):
        parser.error("--port and --program are needed to declare a service")

    try:
        manifest = read_manifest(args.manifest)
    except ManifestError as refused:
        parser.error(str(refused))
    name = args.name or manifest["name"] or args.subdomain
    G.set_namespace(args.namespace)
    kit, declared = build(manifest, args.subdomain, name, args.port, args.program)

    seed = G.load_seed(args.into)
    if not args.no_backup:
        shutil.copyfile(args.into, args.into + ".bak")
    if args.remove:
        things, relationships = G.remove_from(kit, seed)
        print(f"Removed {things} things, {relationships} relationships from {seed.get('Name', args.into)}.")
    else:
        found = already_declared(seed, declared, args.subdomain)
        for line in describe(seed, kit, declared, found):
            print(line)
        things, relationships = G.merge_into(kit, seed, found)
        print(f"Merged into {seed.get('Name', args.into)}: +{things} things, +{relationships} relationships.")
    G.dump_seed(seed, args.into)


if __name__ == "__main__":
    main()
