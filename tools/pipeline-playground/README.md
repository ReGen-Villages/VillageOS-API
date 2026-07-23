# pipeline-playground

A generator for example **DAGs and services** to exercise the Trellis **Pipeline page** — the service
palette, wiring, field-mapping, transforms, param binding, fan-out, pre-run validation, run history, and
live node status. It emits VOS Things + Relationships in the seed JSON shape and can drop them into any
model.

Stock Python 3.10+, no third-party dependencies. For *using* the generated pipelines in the GUI, see
[docs/PIPELINE_PLAYGROUND.md](../../docs/PIPELINE_PLAYGROUND.md).

## Files

| File | What it is |
| --- | --- |
| `catalog.py` | The content: the service catalog (`SERVICES`) and the example pipelines (`PIPELINES`). Edit this to add or change services and DAGs. |
| `generate.py` | Turns the catalog into Things + Relationships and writes a standalone seed or merges into an existing one. |
| `validate_fragment.py` | Reimplements the Trellis + Phloem graph traversals and asserts every pipeline resolves and passes pre-run validation. |

## Use it

```bash
# a standalone seed you can load on its own to play with the Pipeline page
python3 generate.py --out PipelinePlayground.seed.json

# drop the playground into any model seed, in place (idempotent; writes a .bak alongside)
python3 generate.py --into /path/to/Some.seed.json

# ...with per-model ids so the same DAGs in two seeds don't collide when both load into one broker
python3 generate.py --into /path/to/Some.seed.json --namespace SomeModel

# take it back out again (leaves the shared archetypes/predicates in place; pass the same --namespace)
python3 generate.py --into /path/to/Some.seed.json --remove

# check the output resolves the way Trellis and Phloem read it
python3 validate_fragment.py PipelinePlayground.seed.json
```

## How it drops into any model

Every id is a deterministic UUIDv5 of a stable key, so a re-run replaces the same Things instead of forking
duplicates — merging is idempotent. When merging, the built-in `is` / `has` / `of` predicates and the
pipeline archetypes (`Pipeline`, `PipelineNode`, `Port`, `Service`, `PlatformServiceConnection`,
`PipelineWire`, …) are reconciled **by name** against the target: an existing `is` is reused, never doubled.
Everything else is namespaced so it will not collide with a host model's own Things.

Those ids are the same in every seed by default, which is what you want for a standalone fragment but not
when two model seeds carrying the playground load into one broker together — they would collide. Pass
`--namespace <ModelName>` when merging to derive a per-model id root, so each seed's copy of the DAGs gets
its own ids. Reconciliation stays name-based, so the shared vocabulary still folds onto the target's own
`is` / `has` / archetypes either way. Use the same `--namespace` with `--remove`.

## What's in the catalog

**Services** (each a palette entry — a `PlatformServiceConnection` carrying a dispatch `Subdomain`, bound to
a `Service` that declares typed ports): text and data transforms (Echo, Uppercase, Reverse, JSON Merge,
Format Report), sources (Generate Text, Timer Tick, Random Number, Site Parameters), arithmetic (Multiply,
Sum), the site-analysis domains (Water Reserve, Energy Balance, Metabolism, Model Bridge), a fan-out Scorer
and Batch Enrich, and a Publish sink. Subdomains match live Managed Microservices where one exists
(`echo`, `water-reserve`, `energy-balance`, `metabolism`, `model-bridge`), so a saved pipeline can also be
Run against a live platform; the rest are dispatch-only stand-ins that still drive the editor end to end.

**Pipelines** — each showcases a Pipeline-page feature:

| Pipeline | Shows |
| --- | --- |
| Hello Echo | The canonical linear DAG, plus a seeded succeeded + failed run so the History panel and node status rings work with no live Phloem. |
| Boundary I/O Echo | `PipelineInput` / `PipelineOutput` boundary nodes; the Input's output is filled from a run param, the Output's input becomes the run result. |
| Text Studio | A multi-stage transform chain driven from a run parameter. |
| Field Merge | Two sources landing at different to-paths of one input, deep-merged. |
| Wire Transform | A JSONata transform reshaping the payload on the wire. |
| Diamond Fan | A wider fan-out/fan-in DAG — layout + multi-input merge + report. |
| Fan-out Scoring | A collection input that spreads into one run item each, `onItemError=continue`, with a seeded partial run showing fan-out progress. |
| Water / Energy Self-Sufficiency | Param-bound site-analysis nodes. |
| Site Analysis (Combined) | A capstone: a source feeding one wired input while others are param-bound, two analyses in parallel, merge → report → sink. |
| Number Cruncher | A numeric chain mixing wired and param-bound inputs on the same node. |
| Enrichment Batch | Fan-out into a sink. |

## The graph shape it writes

Mirrors `vos.Infrastructure.Tests/Fixtures/pipeline-demo.seed.json` and the contract read by
`vos.Trellis/src/pipeline/model.ts` and `vos.ManagedMicroservice.Phloem`:

- A **pipeline** `is Pipeline` and `has` its nodes.
- A **node** `is PipelineNode` and `has` a `PlatformServiceConnection` (a boundary node instead `is`
  `PipelineInput` / `PipelineOutput` and declares its own `has → Port` children). Node canvas position rides
  as `x` / `y`; a param binding rides as a `paramBindings` JSON property.
- A **connection** `is PlatformServiceConnection`, carries a `Subdomain`, and `has` a `Service`; the service
  `has` its `Port` children (`portName` / `direction` / `type` / `required` / `collection`).
- A **wire** is the `feeds` predicate (which `is PipelineWire`), carrying `fromPort` / `toPort` and optional
  `fromPath` / `toPath` / `transform`.
- A seeded **run** `is PipelineRun`, points `of` its pipeline, and `has` `NodeRun` children (a per-item
  `NodeRun` carrying `index` / `total` drives fan-out progress).
