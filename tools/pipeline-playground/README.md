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
| `declare_service.py` | Declares one service in a seed from its `GET /manifest`, with a published pipeline that runs it. |
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

## Declare one service from its manifest

A pipeline can use a service only when the model declares it: the service, its connection, its ports and
the vocabulary Phloem refuses to work without. `declare_service.py` writes all of that into a seed in place
from what the service says about itself at `GET /manifest`, plus what only the deployment knows: the
subdomain the platform forwards calls to it under, the port it is started on and the program it is started
from.

```bash
# from a saved manifest, or from a running service (--manifest http://localhost:5110/manifest)
python3 declare_service.py --manifest quadrat.manifest.json --subdomain quadrat --port 5110 \
    --program /srv/quadrat/app.py --into /path/to/Some.seed.json

# take it out again, with the same --manifest, --subdomain and --namespace it went in with
python3 declare_service.py --manifest quadrat.manifest.json --subdomain quadrat --into /path/to/Some.seed.json --remove
```

**What the manifest carries.** Either the bare list of ports an older service answers, or an object:

```json
{
  "service": "Quadrat",
  "rehearses": true,
  "ports": [{"portName": "raster", "direction": "in", "type": "string", "required": true},
            {"portName": "result", "direction": "out", "type": "object"}],
  "settings": [{"name": "operation", "required": true, "values": ["metadata", "point", "zonal"]},
               {"name": "resampling", "values": ["nearest", "bilinear"]}]
}
```

- `service` names the service; `--name` overrides it, and the subdomain stands in when both are absent.
- `rehearses` says the service writes nothing but its answer, so a rehearsal may call it. Absent, the
  service does not rehearse.
- each port names itself and its direction (`in` or `out`); `type` (empty is a wildcard), `required` and
  `collection` are read as the catalog's ports are.
- each setting is a value the node carries and the service reads from `params`, not from a port. The
  node is written with the setting's `default`, else the first of its `values` when it is `required`, else
  empty — declared empty because a property write on a Thing updates and never creates.

**What it writes, or confirms the seed already holds:** the service (its program path, port and
`rehearses`), its connection triggered over http and carrying the subdomain, one Port Thing per manifest
port, and every role Phloem requires, bound. Then a published pipeline: an Input node filling each
required in port, the service node carrying the settings, a wire from each out port to the Output node
taken on success, and the error port wired to the Output node taken on fail. The seed is edited in place
with a `.bak` beside it, as `generate.py --into` does, and running it a second time changes nothing.

**A seed that already declares the service keeps its own Things.** The connection is found by its subdomain,
the service as what the connection holds, and each port by its name and direction, on the service or a kind
above it; what the manifest lists and the seed lacks is added, and the rest is left as it is. The
vocabulary reconciles as the playground's does: through what the seed's role Things bind, then by name. The
tool prints what it found and what it added. `--remove` takes out what it added and leaves the seed's own
Things and the shared vocabulary.

## How it drops into any model

Every id is a deterministic UUIDv5 of a stable key, so a re-run replaces the same Things instead of forking
duplicates — merging is idempotent. When merging, the target's own vocabulary is reused rather than doubled:
the `is` / `has` / `of` predicates are matched **by name**, and each archetype or predicate playing a role is
matched **through what the target's role Thing binds**, then by name — a match by name passes over role
Things — so a model that calls its pipeline
archetype something else keeps its own. One the target holds under the same name but has never bound comes
to play the role as it reconciles, and the target's own role Things are kept — the binding is what says
what an archetype is for, and the editor and the orchestrator both read it. A model that binds its own stop
kind keeps that kind's ports, and one that binds its own `stops` connection keeps that connection's
service: the playground adds neither, which would leave a required port unfilled on every stop node or
send each stop request to a second service. Everything else is namespaced so it will not collide with a
host model's own Things.

The playground also declares the three roots every archetype in a model sits under, `Configuration`,
`Operation` and `Predicate`, bound by the `configuration`, `operation` and `predicate` roles, and places
what it mints: the pipeline, node, port, wire, service, connection, external-system, message-kind and
strategy archetypes under `Configuration`; every predicate it declares under `Predicate`; every role
Thing `is Role`, and `Role` is under `Configuration`; the run, node-run, sent-message and stop-request archetypes,
which record what a run did, under `Operation`. Merged into a model that already binds the roots, the placements land on that model's own
roots, like the rest of the shared vocabulary. Merged into one that does not, the roots arrive with the
playground, and seed validation then holds the whole model to them.

Those ids are the same in every seed by default, which is what you want for a standalone fragment but not
when two model seeds carrying the playground load into one broker together — they would collide. Pass
`--namespace <ModelName>` when merging to derive a per-model id root, so each seed's copy of the pipelines gets
its own ids. Reconciliation stays name-based, so the shared vocabulary still folds onto the target's own
`is` / `has` / archetypes either way. Role Things are the one set of identifiers `--namespace` does not
change, because every model declares them alike. Use the same `--namespace` with `--remove`.

## What's in the catalog

**Services** (each a palette entry — a `PlatformServiceConnection` carrying the `Subdomain` it is called at, bound to
a `Service` that declares typed ports): text and data transforms (Echo, Uppercase, Reverse, JSON Merge,
Format Report), sources (Generate Text, Timer Tick, Random Number, Site Parameters), arithmetic (Multiply,
Sum), the site-analysis domains (Water Reserve, Energy Balance, Metabolism, Model Bridge), a fan-out Scorer
and Batch Enrich, a Decide block whose `strategy` port offers three Things to choose from, and a Publish
sink. Subdomains match a shipped service where one exists
(`echo`, `water-reserve`, `energy-balance`, `metabolism`, `model-bridge`), so a saved pipeline can also be
Run against a live platform; the rest are stand-ins with no service behind them that still drive the editor end to end.

**External systems and message kinds** (`EXTERNAL_SYSTEMS`, `MESSAGE_KINDS`) — what stands outside the
platform and what crosses its boundary: a `Sensor gateway` that sends a batch of readings, a `Reporting
office` that is told the daily report. A system `sends` or is `told` kinds of message; a kind `arrivesAt`
a connection; every connection is `triggeredBy` `http`. The `http` and `graph` triggers are each bound
by their role (`httpTrigger`, `graphTrigger`), as the platform recognises a trigger only through its
role. A **cadence** `is Cadence`, carries `intervalMinutes` and `lastDue` (declared empty), and a start
node may stand for it: `Every hour` starts the Hourly Tally drawing.

**Pipelines** — each showcases a Pipeline-page feature:

| Pipeline | Shows |
| --- | --- |
| Hello Echo | The canonical linear pipeline, plus a seeded succeeded + failed run so the History panel and node status rings work with no live Phloem. |
| Boundary I/O Echo | `PipelineInput` / `PipelineOutput` boundary nodes; the Input's output is filled from a run param, the Output's input becomes the run result. |
| Text Studio | A multi-stage transform chain driven from a run parameter. |
| Field Merge | Two sources landing at different to-paths of one input, deep-merged. |
| Wire Transform | A JSONata list transform reshaping the payload on the wire. |
| Diamond Fan | A wider fan-out/fan-in pipeline — layout + multi-input merge + report. |
| Fan-out Scoring | A collection input that spreads into one run item each, `onItemError=continue`, with a seeded partial run showing fan-out progress. |
| Water / Energy Self-Sufficiency | Param-bound site-analysis nodes. |
| Site Analysis (Combined) | A capstone: a source feeding one wired input while others are param-bound, two analyses in parallel, merge → report → publish → Output. |
| Number Cruncher | A numeric chain mixing wired and param-bound inputs on the same node. |
| Enrichment Batch | Fan-out, then publish, ending at the Output node. |
| Readings Arrive | A start standing for the kind of message the Sensor gateway sends, ending at the answer. |
| Daily Report | A tell node, The office is told, tells the Reporting office the report and passes it on to the Output node, Reported. |
| Branching Decision | A node holding the choices `accept` and `refuse`; `accept` is taken when the strategy it was given is Nearest first or Cheapest first, `refuse` otherwise; the other branch is recorded not taken and the Output node is reached once. |
| Hourly Tally | A start standing for the cadence `Every hour`, its `due` port wired into Echo, ending at `tally`. |
| Echo and Follow On | A follow-on node, Boundary I/O Echo follows, starts Boundary I/O Echo with the seed Echo answered as that run's `seed`, and passes the seed on to the Output node. |
| Stop the Runs About Them | A stop node, Stop their runs, stops the runs about each Thing given in `things`, carrying `reason`, and passes on how many stop requests it wrote as `requested`. |
| Each Item Reshaped | An item transform on the wire between two enrichment nodes that run once per item, so each record goes on reshaped without waiting for slower ones. |

Every drawing is published. Every service rehearses except Water Reserve, Energy Balance, Metabolism and
Model Bridge, which nobody has yet checked to write nothing.

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
- A **wire** is a Thing of the wire archetype that its node `has`. It reaches the node it feeds along
  `carries`, the out port it takes its value from along `inputConnector` and the in port it delivers to
  along `outputConnector`, each predicate bound by a role of the same word, and carries `fromPath`,
  `toPath`, `listTransform` and `itemTransform` and no port names. A wire drawn as a relationship
  between two nodes is refused.
- A **stop node** `is` the stop kind, which the `pipelineStop` role binds, and holds no ports of its own:
  its `things`, `reason` and `requested` ports are the kind's. A **stop request** `is StopRequest`,
  carries `reason`, names the run that wrote it along `requestedBy`, and is related along `stops` to
  what it stops — a run, or a Thing whose runs stop. `stops` is a connection as well as a predicate, bound
  by the `runStopping` role: the `graph` trigger fires it and it `has` the orchestrator service, so the
  request reaches the orchestrator. A stopped run relates along `stoppedBy` to the request.
- A seeded **run** `is PipelineRun`, points `of` its pipeline, and `has` `NodeRun` children (a per-item
  `NodeRun` carrying `index` / `total` drives fan-out progress).
- An **external system** `is ExternalSystem` and `sends` or is `told` message kinds; a **message kind**
  `is MessageKind` and `arrivesAt` a connection; a **boundary node** `standsFor` the Thing it stands for
  — a connection, a state, a message kind or an external system. Each of these predicates and
  archetypes is bound by its role Thing (`sends`, `told`, `arrivesAt`, `standsFor`, …), so the page
  reads roles and never names.
