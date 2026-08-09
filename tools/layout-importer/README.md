# layout-importer

Build a **containment tree** from a file and apply it as one model fragment.

A *layout* is a nested physical structure: something contains areas, those contain sub-areas, and
somewhere down the chain are the places things stand. This tool turns a description of one into
Things, containment edges and `is` edges, and puts it into a model.

It is **domain-agnostic**. The level names, the containment predicate and the type each level
belongs to are all named by the caller, so the tool carries no vocabulary of its own. A consumer's
own structure and naming standard live in the consumer's repository, the way a scenario lives beside
`tools/simulator` rather than inside it.

No third-party dependencies — stock Python 3.10+.

## Why a tool

The measurements come off a drawing, and a drawing changes more than once during a programme.
Hand-authoring a structure is work nobody should do twice, and it is not the kind of work that gets
done carefully the second time.

## Names carry the structure

Most naming standards write position into a name. Given a pattern with one named group per level,
the tool derives every intermediate node from the leaf names — **a node's name is the leaf name up
to the end of its group**, so no separator is assumed and nothing intermediate has to be listed.

```
S-01-02   →   S        (site)
              S-01     (room)
              S-01-02  (cell)
```

The file then lists only what the names cannot say.

## The file

```json
{
  "name": "sample structure",
  "containsPredicate": "holds",
  "levels": [
    { "name": "site", "type": "Site" },
    { "name": "room", "type": "Room", "properties": { "height_m": 6 } },
    { "name": "cell", "type": "Cell", "properties": { "capacity_units": 100 } }
  ],
  "pattern": "(?P<site>[A-Z]+)-(?P<room>\\d{2})-(?P<cell>\\d{2})",
  "leaves": ["S-01-01", "S-01-02", "S-02-01"],
  "nodes": [
    { "name": "S-01-01", "properties": { "capacity_units": 250 }, "types": ["ColdCell"] },
    { "name": "odd-one", "level": "cell", "within": "S-01" }
  ]
}
```

| Key | What it is |
| --- | --- |
| `containsPredicate` | The predicate the containment is written with. Yours, not the tool's. |
| `levels` | The depth, outermost first. Each names the type its nodes are, and the properties every node of it carries unless it says otherwise. |
| `pattern` | One named group per level. A leaf name that does not match is reported with its line rather than guessed at. |
| `leaves` | The names at the bottom of the structure. Everything above them is derived. |
| `nodes` | Overrides, field by field — a different measurement, an extra type, or a node the names do not reach (give it a `level`, and a `within` if it has a parent). |

## Running it

```bash
# See what it would send
python3 layout.py layout.json --ids ids.json --out fragment.json

# Send it
python3 layout.py layout.json --url https://localhost:7243 --api-key "$VOS_API_KEY"
```

The predicate and type **names** in the file are resolved to model ids, either from an `--ids` file
or from the model itself; where both answer, the file wins. The tool never guesses one: a wrong id
would quietly build a second structure beside the real one, so a name it cannot resolve is reported
instead. A dry run with `--ids` covering every name never contacts the model at all.

A file that cannot be read fails before any of that, so a typo costs no round trip.

## Running it again

Ids come from names via `stable_id`, and `POST /api/model/fragment` is an idempotent upsert. So a
second run **adds what is new and disturbs nothing else** — a node keeps its identity, and whatever
was standing in it still points at it. Extending a structure is editing the file and running again.

## Tests

```bash
python3 -m unittest test_layout
```

Names building the tree, a name that does not follow the standard being reported, a row overriding
one field and leaving the rest, the fragment carrying every node once with its containment and type
edges, ids staying stable, an extended file keeping the ids the first run used, applying sending one
fragment and a second run sending the same one, and a file that cannot be read failing before the
model is asked anything.

Everything but the command-line entry point is covered. `python3 -m coverage run --source=layout -m
unittest test_layout` reports it.
