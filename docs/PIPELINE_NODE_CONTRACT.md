# Pipeline Node Contract (DAG node envelope)

How a managed microservice opts into being a **node in a pipeline DAG** (Feature #5628). This is an
**additive** layer over the [Microservice Contract](MICROSERVICE_CONTRACT.md): the same `POST /handle`,
the same JWT, the same registration. A node is just a service that, in addition to its normal graph/http
handling, recognises one extra request shape — the **node envelope** — and answers it with **outputs**.

The orchestrator ([Phloem](MICROSERVICES.md)) executes a pipeline by invoking each node through Mycelium
with this envelope and routing each node's outputs to its downstream inputs.

## The envelope

The orchestrator posts to a node's existing `/handle`:

```jsonc
// orchestrator → node
POST /handle
{
  "runId":  "<guid>",          // the PipelineRun this invocation belongs to
  "nodeId": "<guid>",          // the PipelineNode being executed
  "params": { /* static, node-authored params */ },
  "inputs": {                  // keyed by INPUT-port name
    "message": "hello",                                   // an in-band literal, or…
    "geometry": { "ref": { "thingId": "<guid>", "property": "mesh" } }  // …a graph reference
  }
}
```

The node replies:

```jsonc
// node → orchestrator
{
  "success": true,
  "outputs": { "echo": "hello" },   // keyed by OUTPUT-port name; literals or refs
  "error": null
}
```

A request is a node invocation **iff it carries both `runId` and `nodeId`**. Anything else is a legacy
graph/http `/handle` body and the service handles it exactly as before — the two never collide.

## Reference inputs

Large values (geometry, datasets) are not shipped in-band. Instead an input may be a **reference**
`{ "ref": { "thingId", "property" } }`; the node resolves it to the live value via
`GET /api/things/{thingId}/effective-properties` before running. Output the same shape to hand a large
value to a downstream node. Small values (numbers, strings, ThingRefs) travel in-band.

## Ports and `/manifest`

A node advertises typed **ports** — `{ portName, direction: in|out, type, required }` — so the editor can
type-check wires and the model's `Port` Things can be seeded. A node service may expose:

```jsonc
GET /manifest → [ { "portName": "message", "direction": "in",  "type": "string", "required": true },
                  { "portName": "echo",    "direction": "out", "type": "string", "required": false } ]
```

## .NET SDK base

`DagNodeService` (in `vos.ManagedMicroservice.Shared`, namespace `vos.ManagedMicroservice.Shared.DagNode`)
implements the envelope so a service only writes business logic:

```csharp
public sealed class MyNode : DagNodeService
{
    public MyNode(IHttpClientFactory f, ILogger<MyNode> l, string myceliumUrl, string? token)
        : base(f, l, myceliumUrl, token) { }

    public override IReadOnlyList<PortDescriptor> Ports { get; } = new[]
    {
        PortDescriptor.Input("message", "string", required: true),
        PortDescriptor.Output("echo", "string"),
    };

    protected override Task<NodeResult> ExecuteNodeAsync(NodeContext ctx, CancellationToken ct)
    {
        var message = ctx.Input("message")?.GetString() ?? throw new InvalidOperationException("message required");
        return Task.FromResult(NodeResult.Ok(("echo", message)));
    }
}
```

Wire it into the host's existing `/handle`:

```csharp
app.MapPost("/handle", async (HttpContext http, MyNode node) =>
{
    using var doc = await JsonDocument.ParseAsync(http.Request.Body);
    var root = doc.RootElement;
    return DagNodeService.IsNodeEnvelope(root)
        ? Results.Ok(await node.HandleNodeAsync(root, http.RequestAborted))   // run as a DAG node
        : Results.Ok(/* … the service's existing graph/http handling … */);
});
```

`HandleNodeAsync` parses the envelope, resolves any `ref` inputs, runs `ExecuteNodeAsync`, and shapes
`{success,outputs,error}`. A throw inside the node becomes `{ "success": false, "error": "…" }` — never an
unhandled 500 — so the orchestrator can record the node failure and halt dependents cleanly.

> Languages other than .NET implement the same envelope directly — it is plain JSON over the existing
> `/handle`, identical in spirit to the base contract's relationship body.
