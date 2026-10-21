# pipeline-playground

A generator for example **pipelines and services** to exercise the Trellis **Pipelines page** — the service
palette, wiring, field-mapping, transforms, param binding, fan-out, pre-run validation, run history, and
live node status. It emits VOS Things + Relationships in the seed JSON shape and can drop them into any
model.

Stock Python 3.10+, no third-party dependencies. For *using* the generated pipelines in the GUI, see
[docs/PIPELINE_PLAYGROUND.md](../../docs/PIPELINE_PLAYGROUND.md).

## Files

| File | What it is |
| --- | --- |
| `catalog.py` | The content: the service catalog (`SERVICES`) and the example pipelines (`PIPELINES`). Edit this to add or change services and pipelines. |
| `generate.py` | Turns the catalog into Things + Relationships and writes a standalone seed or merges into an existing one. |
| `validate_fragment.py` | Reimplements the Trellis + Phloem graph traversals and asserts every pipeline resolves and passes pre-run validation. |

## Use it

```bash
# a standalone seed you can load on its own to play with the Pipeline page
python3 generate.py --out PipelinePlayground.seed.json

# drop the playground into any model seed, in place (idempotent; writes a .bak alongside)
python3 generate.py --into /path/to/Some.seed.json

# ...with per-model ids so the same pipelines in two seeds don't collide when both load into one broker
python3 generate.py --into /path/to/Some.seed.json --namespace SomeModel

# take it back out again (leaves the shared archetypes, predicates, role Things and what they bind in place; pass the same --namespace)
python3 generate.py --into /path/to/Some.seed.json --remove

# check the output resolves the way Trellis and Phloem read it
python3 validate_fragment.py PipelinePlayground.seed.json
```

## How it drops into any model

Every id is a deterministic UUIDv5 of a stable key, so a re-run replaces the same Things instead of forking
duplicates — merging is idempotent. When merging, the target's own vocabulary is reused rather than doubled:
the `is` / `has` / `of` predicates are matched **by name**, and each archetype or predicate playing a role is
matched **through what the target's role Thing binds**, then by name, so a model that calls its pipeline
archetype something else keeps its own. One the target holds under the same name but has never bound comes
to play the role as it reconciles, and the target's own role Things are kept — the binding is what says
what an archetype is for, and the editor and the orchestrator both read it. Everything else is namespaced
so it will not collide with a host model's own Things.

The playground also declares the two roots every archetype in a model sits under, `Configuration` and
`Operation`, bound by the `configuration` and `operation` roles, and places what it mints: the pipeline,
node, port, wire, service, connection, external-system, message-kind and strategy archetypes and every
predicate and role Thing under `Configuration`; the run, node-run and sent-message archetypes, which record
what a run did, under `Operation`. Merged into a model that already binds the roots, the placements land on that model's own
roots, like the rest of the shared vocabulary. Merged into one that does not, the roots arrive with the
playground, and seed validation then holds the whole model to them.

Those ids are the same in every seed by default, which is what you want for a standalone fragment but not
when two model seeds carrying the playground load into one broker together — they would collide. Pass
`--namespace <ModelName>` when merging to derive a per-model id root, so each seed's copy of the pipelines gets
its own ids. Reconciliation stays name-based, so the shared vocabulary still folds onto the target's own
`is` / `has` / archetypes either way. Role Things are the one set of identifiers `--namespace` does not
change, because every model declares them alike. Use the same `--namespace` with `--remove`.

## What's in the catalog

**Services** (each a palette entry — a `PlatformServiceConnection` carrying a dispatch `Subdomain`, bound to
a `Service` that declares typed ports): text and data transforms (Echo, Uppercase, Reverse, JSON Merge,
Format Report), sources (Generate Text, Timer Tick, Random Number, Site Parameters), arithmetic (Multiply,
Sum), the site-analysis domains (Water Reserve, Energy Balance, Metabolism, Model Bridge), a fan-out Scorer
and Batch Enrich, a Decide block whose `strategy` port offers three Things to choose from, and a Publish
sink. Subdomains match a shipped service where one exists
(`echo`, `water-reserve`, `energy-balance`, `metabolism`, `model-bridge`), so a saved pipeline can also be
Run against a live platform; the rest are dispatch-only stand-ins that still drive the editor end to end.

**External systems and message kinds** (`EXTERNAL_SYSTEMS`, `MESSAGE_KINDS`) — what stands outside the
platform and what crosses its boundary: a `Sensor gateway` that sends a batch of readings, a `Reporting
office` that is told the daily report. A system `sends` or is `told` kinds of message; a kind `arrivesAt`
a connection; every connection is `triggeredBy` `http`.

**Pipelines** — each showcases a Pipeline-page feature:

| Pipeline | Shows |
| --- | --- |
| Hello Echo | The canonical linear pipeline, plus a seeded succeeded + failed run so the History panel and node status rings work with no live Phloem. |
| Boundary I/O Echo | `PipelineInput` / `PipelineOutput` boundary nodes; the Input's output is filled from a run param, the Output's input becomes the run result. |
| Text Studio | A multi-stage transform chain driven from a run parameter. |
| Field Merge | Two sources landing at different to-paths of one input, deep-merged. |
| Wire Transform | A JSONata transform reshaping the payload on the wire. |
| Diamond Fan | A wider fan-out/fan-in pipeline — layout + multi-input merge + report. |
| Fan-out Scoring | A collection input that spreads into one run item each, `onItemError=continue`, with a seeded partial run showing fan-out progress. |
| Water / Energy Self-Sufficiency | Param-bound site-analysis nodes. |
| Site Analysis (Combined) | A capstone: a source feeding one wired input while others are param-bound, two analyses in parallel, merge → report → sink. |
| Number Cruncher | A numeric chain mixing wired and param-bound inputs on the same node. |
| Enrichment Batch | Fan-out into a sink. |
| Readings Arrive | A start standing for the kind of message the Sensor gateway sends, ending at the answer. |
| Daily Report | A pipeline ending at the Reporting office, which is told the report. |
| Branching Decision | A node holding out ports `accept` and `refuse` of its own and answering under one; the other branch is recorded not taken and the end is reached once. |

## The graph shape it writes

The contract read by `vos.Trellis/src/pipeline/model.ts` and `vos.Service.Phloem`:

- A **pipeline** `is Pipeline` and `has` its nodes.
- A **node** `is PipelineNode` and `has` a `PlatformServiceConnection` (a boundary node instead `is`
  `PipelineInput` / `PipelineOutput` and declares its own `has → Port` children). A service node may also
  `has` Port Things of its own, and an in port offers Things along the predicate the `choice` role binds,
  the node relating to the one chosen along the same predicate. Node canvas position rides
  as `x` / `y`; a parameter binding rides as a `parameterBindings` JSON property.
- A **connection** `is PlatformServiceConnection`, carries a `Subdomain`, and `has` a `Service`; the service
  `has` its `Port` children (`portName` / `direction` / `type` / `required` / `collection`).
- A **wire** is the `carries` predicate (which `is PipelineWire`), carrying `fromPort` / `toPort` and optional
  `fromPath` / `toPath` / `transform`.
- A seeded **run** `is PipelineRun`, points `of` its pipeline, and `has` `NodeRun` children (a per-item
  `NodeRun` carrying `index` / `total` drives fan-out progress).
- An **external system** `is ExternalSystem` and `sends` or is `told` message kinds; a **message kind**
  `is MessageKind` and `arrivesAt` a connection; a **boundary node** `standsFor` the Thing it stands for
  — a connection, a state, a message kind or an external system. Each of these predicates and
  archetypes is bound by its role Thing (`sends`, `told`, `arrivesAt`, `standsFor`, …), so the page
  reads roles and never names.
