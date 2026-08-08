#!/usr/bin/env python3
"""Build a containment tree from a file and apply it as one model fragment.

A *layout* is a nested physical structure: something contains areas, those contain sub-areas, and
somewhere down the chain are the places things stand. This tool turns a description of one into
Things, containment edges and `is` edges, and puts it into a model.

It knows no domain. The level names, the containment predicate and the type each level belongs to
are all named by the caller, so nothing here carries a vocabulary of its own.

Two things make it worth a tool rather than a script. **Names usually carry the structure** — a
naming standard that encodes position lets the whole tree be derived from the leaf names, so the
file lists only what the names cannot say. And **it is re-runnable**: ids come from names, and the
fragment endpoint is an idempotent upsert, so running it again to extend a structure adds what is
new and leaves everything else — including whatever is standing in it — exactly as it was.

Stock Python 3.10+, no third-party dependencies, like tools/simulator beside it.
"""
from __future__ import annotations

import argparse
import json
import pathlib
import re
import sys
from dataclasses import dataclass, field

# The Mycelium client lives with the simulator; one client for the tools, not one each.
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent / "simulator"))

from mycelium import MyceliumClient, stable_id

IS_PREDICATE = "is"


class LayoutError(Exception):
    """A layout that cannot be read. The message names the file's own words — a line, a name, a
    level — because whoever wrote the file is the one who has to fix it."""


@dataclass(frozen=True)
class Level:
    """One depth of the structure: what it is called, the type its nodes are, and the property
    values every node of it carries unless it says otherwise."""

    name: str
    type_name: str
    properties: dict = field(default_factory=dict)


@dataclass
class Node:
    """One thing in the structure. `name` is the literal prefix of the leaf names beneath it, so a
    node's name is what the naming standard already calls it."""

    name: str
    level: Level
    parent: "Node | None" = None
    properties: dict = field(default_factory=dict)
    extra_types: list = field(default_factory=list)

    @property
    def id(self) -> str:
        return stable_id("layout", self.level.name, self.name)

    @property
    def type_names(self) -> list:
        return [self.level.type_name, *self.extra_types]


@dataclass(frozen=True)
class Layout:
    """A layout as read from its file, before anything is sent anywhere."""

    name: str
    contains_predicate: str
    nodes: list


def read_layout(spec: dict) -> Layout:
    """Turn a spec into a tree.

    The leaf names and the pattern do the work: each named group in the pattern is one level, and a
    node's name is the leaf name up to the end of that group — so a standard that writes position
    into a name gives the whole tree without listing an intermediate node anywhere. Rows in `nodes`
    then override, field by field, what the pattern and the level defaults derived.
    """
    levels = _read_levels(spec)
    pattern = _read_pattern(spec, levels)
    contains = _required(spec, "containsPredicate")

    by_name: dict = {}
    ordered: list = []

    for line, leaf in enumerate(spec.get("leaves", []), start=1):
        match = pattern.fullmatch(leaf)
        if match is None:
            raise LayoutError(
                f"line {line}: '{leaf}' does not match the naming pattern, so its place in the "
                f"structure cannot be read. Either correct the name or list it under 'nodes'."
            )
        parent = None
        for level in levels:
            end = match.end(level.name)
            name = leaf[:end]
            node = by_name.get(name)
            if node is None:
                node = Node(name=name, level=level, parent=parent, properties=dict(level.properties))
                by_name[name] = node
                ordered.append(node)
            elif node.level is not level:
                raise LayoutError(
                    f"line {line}: '{name}' is read as {level.name} here and as {node.level.name} "
                    f"elsewhere; one name cannot be two levels."
                )
            parent = node

    for row in spec.get("nodes", []):
        _apply_row(row, by_name, ordered, levels)

    return Layout(name=spec.get("name", "layout"), contains_predicate=contains, nodes=ordered)


def _read_levels(spec: dict) -> list:
    raw = _required(spec, "levels")
    if not raw:
        raise LayoutError("'levels' is empty: a structure needs at least one level.")
    levels = []
    for position, entry in enumerate(raw, start=1):
        name = _required(entry, "name", f"level {position}")
        levels.append(Level(name=name,
                            type_name=_required(entry, "type", f"level '{name}'"),
                            properties=dict(entry.get("properties", {}))))
    return levels


def _read_pattern(spec: dict, levels: list) -> re.Pattern:
    pattern = re.compile(_required(spec, "pattern"))
    missing = [level.name for level in levels if level.name not in pattern.groupindex]
    if missing:
        raise LayoutError(
            f"the pattern names no group for {', '.join(missing)}, so a name cannot say which "
            f"{missing[0]} it belongs to."
        )
    return pattern


def _apply_row(row: dict, by_name: dict, ordered: list, levels: list) -> None:
    name = _required(row, "name")
    node = by_name.get(name)
    if node is None:
        level = next((one for one in levels if one.name == row.get("level")), None)
        if level is None:
            raise LayoutError(
                f"'{name}' is listed under 'nodes' but no leaf name puts it in the structure; "
                f"give it a 'level' so its place is known."
            )
        parent_name = row.get("within")
        parent = by_name.get(parent_name) if parent_name else None
        if parent_name and parent is None:
            raise LayoutError(f"'{name}' is said to be within '{parent_name}', which is not here.")
        node = Node(name=name, level=level, parent=parent, properties=dict(level.properties))
        by_name[name] = node
        ordered.append(node)
    node.properties.update(row.get("properties", {}))
    node.extra_types.extend(row.get("types", []))


def _required(source: dict, key: str, where: str = ""):
    if key not in source:
        raise LayoutError(f"'{key}' is missing{' from ' + where if where else ''}.")
    return source[key]


def build_fragment(layout: Layout, ids: dict) -> tuple:
    """The Things and Relationships of the whole structure.

    Every node is one Thing carrying its properties, one containment edge to whatever holds it, and
    one `is` edge per type it belongs to. A root has nothing above it, so it gets no containment
    edge. `ids` maps the predicate and type names the spec used to the ids the model holds them
    under — the tool never guesses those, because a wrong guess would quietly build a second
    structure beside the real one.
    """
    unknown = [name for name in names_used(layout) if name not in ids]
    if unknown:
        raise LayoutError(
            f"no id known for {', '.join(unknown)}. Resolve them against the model, or supply them "
            f"with --ids."
        )

    things, relationships = [], []
    for node in layout.nodes:
        things.append(MyceliumClient.fragment_thing(node.id, node.name, node.properties))
        if node.parent is not None:
            relationships.append(MyceliumClient.fragment_rel(
                node.parent.id, ids[layout.contains_predicate], node.id,
                name=f"{node.parent.name} {layout.contains_predicate} {node.name}"))
        for type_name in node.type_names:
            relationships.append(MyceliumClient.fragment_rel(
                node.id, ids[IS_PREDICATE], ids[type_name],
                name=f"{node.name} is {type_name}"))
    return things, relationships


def resolve_ids(client, names) -> dict:
    """The ids the model holds these names under. A name it does not hold is simply absent, and
    build_fragment is what says which absences mattered."""
    found = {}
    for name in sorted(set(names)):
        found_id = client.thing_id_by_name(name)
        if found_id:
            found[name] = found_id
    return found


def names_used(layout: Layout) -> list:
    """Every predicate and type name this structure needs an id for."""
    names = {layout.contains_predicate, IS_PREDICATE}
    for node in layout.nodes:
        names.update(node.type_names)
    return sorted(names)


def main(argv=None, client=None) -> int:
    """`client` is a test seam: given one, nothing here opens a connection."""
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("spec", help="the layout file")
    parser.add_argument("--ids", help="JSON mapping predicate and type names to model ids")
    parser.add_argument("--out", help="write the fragment here instead of sending it")
    parser.add_argument("--url", default="https://localhost:7243", help="Mycelium base URL")
    parser.add_argument("--api-key", dest="api_key")
    parser.add_argument("--insecure", action="store_true")
    args = parser.parse_args(argv)

    with open(args.spec, encoding="utf-8") as handle:
        spec = json.load(handle)

    # Read the file before reaching for the network, so a spec that cannot be read says so straight
    # away rather than after a round trip.
    layout = read_layout(spec)

    ids = {}
    if args.ids:
        with open(args.ids, encoding="utf-8") as handle:
            ids = json.load(handle)

    # A model is needed to send the fragment, and to look up any id the file did not supply. An id
    # the file pins wins over the one the model holds.
    if not args.out or not ids:
        client = client or MyceliumClient(args.url, api_key=args.api_key, insecure=args.insecure)
        ids = {**resolve_ids(client, names_used(layout)), **ids}

    things, relationships = build_fragment(layout, ids)

    if args.out:
        with open(args.out, "w", encoding="utf-8") as handle:
            json.dump({"Name": layout.name, "Things": things, "Relationships": relationships},
                      handle, indent=2)
        print(f"{len(things)} things, {len(relationships)} relationships written to {args.out}")
        return 0

    client.apply_fragment(things, relationships, name=layout.name)
    print(f"{len(things)} things, {len(relationships)} relationships applied as '{layout.name}'")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except LayoutError as error:
        print(f"layout: {error}", file=sys.stderr)
        sys.exit(2)
