# Phloem — the pipeline orchestrator

Phloem runs a pipeline DAG (Feature #5628). It is a managed microservice like any other; everything it does
goes **through Mycelium**. A caller **spawns** a run and **blocks for the result** (synchronous
spawn-and-wait); Phloem dispatches each node the same way it was itself invoked — through Mycelium's
endpoint-forward. See the [Pipeline Node Contract](PIPELINE_NODE_CONTRACT.md) for the node side.

## Spawning a run

Through Mycelium's endpoint-forward (so Phloem is lazy-started, authed, and stat-tracked like any service):

```jsonc
POST /api/endpoints/phloem        // Mycelium forwards to Phloem's /handle
{ "pipelineId": "<guid>", "params": { /* optional run-level params */ } }
```

Phloem runs the whole DAG and returns:

```jsonc
{
  "runId": "<guid>",
  "pipelineId": "<guid>",
  "success": true,
  "nodes": [ { "nodeId": "<guid>", "name": "Generate", "status": "succeeded", "outputs": { "echo": "hi" }, "error": null } ],
  "error": null
}
```

## What a run does

1. **Load** the pipeline's structural closure from Mycelium in one subscription snapshot — Pipeline, nodes,
   their Connections + Services + prototypes, Ports, and wires. The selector explicitly pulls the built-in
   `is`/`has` predicate Things (by name) and every `Port`/`PipelineWire` Thing (by archetype), because a
   snapshot's `includeRelationships` does not carry predicate Things and `includeIsAncestors` does not carry
   a prototype's ports.
2. **Build the DAG.** Each node binds a **Connection** (`PipelineNode ‑has→ Connection ‑has→ Service`); the
   Connection's `Subdomain` is the dispatch address. Ports resolve by walking the bound service's `is`-chain.
   Wires are relationships whose predicate **is a `PipelineWire`** (never matched by the name `feeds`),
   carrying `fromPort`/`toPort`.
3. **Validate** up front: a Kahn topological sort proves acyclicity (distinct from Hyphae's runtime
   oscillation detection) and every wire must connect a real out-port to a type-compatible in-port.
4. **Execute** in dependency order. Nodes whose upstreams are all done run concurrently (bounded). Each node
   is invoked via `POST /api/endpoints/<node-subdomain>` with the envelope `{runId,nodeId,params,inputs}`,
   where `inputs` are the upstream outputs routed per the wires. A node failure (or non-2xx) halts its
   dependents and fails the run.
5. **Persist** `PipelineRun` / `NodeRun` state best-effort (drives the live SSE view in a later phase); a
   persistence hiccup never aborts the run.

## Internals (for contributors)

`IMyceliumGateway` is the seam between orchestration and HTTP, so `PipelineExecutor` is unit-tested without a
network. `PipelineGraph` + `PipelineDagBuilder` turn the snapshot into a `PipelineDag`; `DagValidator` checks
it; `MyceliumGateway` is the HTTP implementation (subscription load, thing/relationship writes,
endpoint-forward dispatch).

> **v1 scope.** Synchronous spawn-and-wait with level-by-level concurrency. Live SSE run animation + cancel,
> run-level param routing, incremental rerun/caching, and fan-out over collections are later phases.
