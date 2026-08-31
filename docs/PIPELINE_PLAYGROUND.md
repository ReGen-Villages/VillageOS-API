# Pipeline Playground — using the example DAGs

A set of ready-made **services and pipelines (DAGs)** for exercising the Trellis
[Pipelines page](TRELLIS.md#74-pipelines-dag-editor) — the service palette, wiring, field mapping,
transforms, parameter binding, fan-out, pre-run validation, run history, and live node status.

They come from a generator (see [tools/pipeline-playground](../tools/pipeline-playground/README.md)); this
guide is about *using* what it produced.

## Where the examples live

The generator dropped the same content into two places:

- **`PipelinePlayground` seed** — a small standalone model (`vos.Mycelium/seeds/PipelinePlayground.seed.json`).
  Load this to work with just the pipelines, nothing else.
- **The `Josudan` seed** — the same services and DAGs merged into the Josudan site model, so you can build
  pipelines alongside real building data.

## Load them

Open the GUI and click **Switch Model** (the ⇄ arrows in the Dashboard or Graph header) to open the seed
picker, then choose **PipelinePlayground** (or **Josudan**). Mycelium replaces the current model with the
seed and re-scopes your session to it.

Now click **Pipelines** in the sidebar. On the left you'll see the **Services** palette populated with the
example services; the **Load pipeline…** dropdown in the toolbar lists the example DAGs.

## The Pipelines page in 60 seconds

- **Services palette (left)** — each entry is a service you can drop as a node. Click one to add it to the
  canvas. **Input** and **Output** buttons drop boundary nodes.
- **Wire ports** — drag from a node's output handle (right) to another node's input handle (left). Wires are
  type-checked; an incompatible connection is refused.
- **Load pipeline…** — open one of the examples. **New** clears the canvas.
- **Run** — spawns the DAG (enabled only once the pipeline is saved and passes validation). **Params** bar
  appears above the canvas when a node binds an input to a run parameter.
- **History** — past runs of the loaded pipeline; selecting one replays its node statuses onto the canvas.
- **Inspector** — click a node to bind its inputs to run parameters (or edit a boundary node's ports); click
  a wire to set field paths and a transform.

Full reference: [TRELLIS.md §7.4](TRELLIS.md#74-pipelines-dag-editor).

## The example DAGs

Load each from the **Load pipeline…** dropdown. Grouped by what it demonstrates.

### Start here

| Pipeline | What it shows | Try |
|----------|---------------|-----|
| **Hello Echo** | The simplest DAG: `Generate Text → Echo`. Ships with a seeded **succeeded** and **failed** run. | Load it, open **History**, and select a run — the node status rings light up with no services running. |

### Boundary nodes and multi-stage flow

| Pipeline | What it shows |
|----------|---------------|
| **Boundary I/O Echo** | `Input → Echo → Output`. The **Input** node's output is seeded from a run parameter; the value wired into the **Output** node becomes the run's published result. Click the Input/Output nodes to see their editable ports. |
| **Text Studio** | A three-stage transform chain: `Input → Uppercase → Reverse → Output`. |

### Field mapping and transforms on wires

Click a **wire** to open its inspector and see the field paths / transform in place.

| Pipeline | What it shows |
|----------|---------------|
| **Field Merge** | Two sources land at different **to-paths** (`greeting`, `roll`) of one `JSON Merge` input, so they deep-merge into one object instead of overwriting. |
| **Wire Transform** | A JSONata **transform** on the `Generate Text → Echo` wire reshapes the payload (`{"message": "shout: " & $}`) before it reaches the input. |
| **Diamond Fan** | A wider DAG — one source fans to two transforms that merge (at to-paths `upper` / `lower`), then a report. Good for seeing canvas layout and multi-input merge. |

### Parameters, fan-out, and the analysis domains

These bind inputs to **run parameters** — the **Params** bar appears above the canvas. See the
[cheat-sheet](#running-a-pipeline) below for values to type.

| Pipeline | What it shows |
|----------|---------------|
| **Fan-out Scoring** | The Scorer's `item` input is a **collection**, so a list parameter spreads into one run item each; `onItemError=continue` collects partial results. Ships with a seeded **partial** run showing 4/5 fan-out progress. |
| **Water Self-Sufficiency** | A `Water Reserve` node with three param-bound inputs. |
| **Energy Self-Sufficiency** | An `Energy Balance` node with every input param-bound. |
| **Number Cruncher** | A numeric chain mixing **wired and param-bound** inputs on the same node: `Random → Multiply (factor param) → Sum (offset param) → Output`. |
| **Site Analysis (Combined)** | The capstone. A `Site Parameters` source feeds one wired input (`population`) while others are param-bound; `Water Reserve` and `Energy Balance` run in parallel, merge into a report, and end at a `Publish` sink. |
| **Enrichment Batch** | Fan-out into a sink: a list of records is enriched one item at a time, then published. |

## Running a pipeline

1. **Load** the pipeline.
2. If a **Params** bar appears, fill it in (see the cheat-sheet). A value that is valid JSON is parsed as
   JSON — so `["a","b","c"]` becomes a list (driving fan-out) and `42` becomes a number; anything else is
   passed as a plain string.
3. Click **Run**. Node status rings animate as the run progresses; the panel at the bottom lists each node's
   status.

**Parameter cheat-sheet** — the keys each pipeline surfaces in the Params bar, with values to try:

| Pipeline | Params to enter |
|----------|-----------------|
| Fan-out Scoring | `items` = `["alpha","beta","gamma","delta","epsilon"]`, `weight` = `2` |
| Water Self-Sufficiency | `population` = `5000`, `perCapitaConsumptionM3` = `55`, `storageCapacityM3` = `40000` |
| Energy Self-Sufficiency | `solarPvAreaM2` = `12000`, `solarResourceKwhPerM2PerYear` = `1400`, `moduleEfficiency` = `0.17`, `performanceRatio` = `0.77`, `otherGenerationMwhPerYear` = `500`, `annualConsumptionMwhPerYear` = `3200` |
| Number Cruncher | `factor` = `10`, `offset` = `5` |
| Site Analysis (Combined) | `perCapitaConsumptionM3` = `55`, `storageCapacityM3` = `40000`, plus the Energy params above |

> **`perCapitaConsumptionM3` is per person per *year*.** `55` is 150 litres per person per day
> expressed annually — the same basis the site-analysis templates use. A per-day figure is silently
> wrong rather than rejected: the calculator derives daily demand itself (`annual ÷ 365`), so
> entering `0.15` understates demand by a factor of about 366 and inflates days of supply by the
> same amount. The calculator's own field is `PerCapitaConsumptionM3PerYear`; the port drops the
> `PerYear`, which is what makes the substitution easy to miss.

### What actually runs vs. what's editor-only

A node dispatches to the **Subdomain** on its connection. Where a real Managed Microservice is running at
that subdomain — **echo**, **water-reserve**, **energy-balance**, **metabolism**, **model-bridge** — the run
produces real output. The other services (Generate Text, Uppercase, Merge, Scorer, Publish, …) are
dispatch-only stand-ins: they exercise the whole editor — drop, wire, validate, save, load, inspect — but a
live Run needs the service listening at that subdomain.

> **water-reserve has to be started by hand.** The site analysis works every figure that service computes
> out for itself, so it declares no Thing for it any more and nothing auto-starts it with the model
> (#6748). The service is still there and still answers over its ports — run it yourself before a live
> Run of Water Self-Sufficiency or Site Analysis (Combined).

So everything on the **canvas** (building, wiring, validation, save/load, field mapping, transforms, boundary
ports) works from seed data alone. A live **Run** with real results needs the target service up; the seeded
run history (Hello Echo, Fan-out Scoring) shows the run/animation UI without one.

> **Boundary `Input` seeding.** An Input node fills its outputs from run parameters matched to its **port
> names**, but the Params bar only surfaces parameters that *service* nodes bind in their inspector — it does
> not add a field for an Input node's ports. To feed a boundary Input a value, spawn the run through Phloem
> with an explicit `params` object (via the API / CLI) rather than the Params bar. In the GUI the boundary
> pipelines are best used to explore the **editor** behaviour.

## Seeded run history

Two pipelines carry pre-recorded runs so the run UI works with nothing else running:

- **Hello Echo** — one succeeded run and one failed run. Load it, open **History**, pick a run, and the node
  rings paint green / red accordingly.
- **Fan-out Scoring** — one partial run where the fan-out node shows **4 of 5** items done.

## Regenerate or customize

The services and DAGs are plain data in
[tools/pipeline-playground/catalog.py](../tools/pipeline-playground/catalog.py) — add a service to `SERVICES`
or a DAG to `PIPELINES` and re-run the generator to refresh the seeds. See the
[tool README](../tools/pipeline-playground/README.md) for the commands (including dropping the playground into
any other model, and taking it back out).
