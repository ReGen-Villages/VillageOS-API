using System.Text.Json;
using Microsoft.Extensions.Logging;
using vos.ManagedMicroservice.Phloem.Configuration;
using vos.ManagedMicroservice.Phloem.Model;

namespace vos.ManagedMicroservice.Phloem.Execution;

/// <summary>
/// Runs a pipeline synchronously (spawn-and-wait): load the subgraph → build the DAG → validate (acyclic +
/// port types) → execute nodes in dependency order, dispatching each through Mycelium and routing each
/// node's outputs to its downstream inputs → return the full result. Independent nodes in a level run
/// concurrently (bounded). Run/NodeRun state is persisted best-effort for the live SSE view.
/// </summary>
public sealed class PipelineExecutor
{
    private static readonly IReadOnlyDictionary<string, JsonElement> NoOutputs =
        new Dictionary<string, JsonElement>();

    private readonly IMyceliumGateway _gateway;
    private readonly PipelineModelOptions _model;
    private readonly ILogger<PipelineExecutor> _logger;
    private readonly int _maxConcurrency;

    public PipelineExecutor(IMyceliumGateway gateway, PipelineModelOptions model, ILogger<PipelineExecutor> logger, int maxConcurrency = 4)
    {
        _gateway = gateway;
        _model = model;
        _logger = logger;
        _maxConcurrency = Math.Max(1, maxConcurrency);
    }

    public async Task<PipelineRunResult> RunAsync(Guid pipelineId, JsonElement runParams, CancellationToken cancellationToken)
    {
        PipelineDag dag;
        try
        {
            var graph = await _gateway.LoadPipelineSubgraphAsync(pipelineId, cancellationToken);
            dag = PipelineDagBuilder.Build(graph, pipelineId, _model);
        }
        catch (PipelineModelException ex)
        {
            return PipelineRunResult.Failed(Guid.Empty, pipelineId, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load pipeline {PipelineId}", pipelineId);
            return PipelineRunResult.Failed(Guid.Empty, pipelineId, $"Failed to load pipeline: {ex.Message}");
        }

        var validation = DagValidator.Validate(dag);
        if (!validation.IsValid)
            return PipelineRunResult.Failed(Guid.Empty, pipelineId, "Pipeline is invalid: " + string.Join(" ", validation.Errors));

        var runId = Guid.NewGuid();
        await BestEffort(() => _gateway.CreateRunAsync(runId, pipelineId, cancellationToken), "create run");

        var outputs = new Dictionary<Guid, IReadOnlyDictionary<string, JsonElement>>();
        var results = new Dictionary<Guid, NodeRunResult>();
        var halted = false;

        using var gate = new SemaphoreSlim(_maxConcurrency);
        var pending = dag.Nodes.ToList();
        while (pending.Count > 0 && !halted)
        {
            var ready = pending.Where(n => dag.WiresInto(n.NodeId).All(w => results.ContainsKey(w.FromNodeId))).ToList();
            if (ready.Count == 0) break; // validated DAG => only reachable when an upstream failed

            var batch = await Task.WhenAll(ready.Select(node => RunNodeGuardedAsync(gate, dag, node, runId, outputs, cancellationToken)));

            foreach (var result in batch)
            {
                results[result.NodeId] = result;
                if (result.Status == RunStatus.Succeeded)
                    outputs[result.NodeId] = result.Outputs;
                else
                    halted = true;
                await BestEffort(() => _gateway.PersistNodeRunAsync(runId, result, cancellationToken), "persist node run");
            }
            pending = pending.Where(n => !results.ContainsKey(n.NodeId)).ToList();
        }

        // Anything still pending was blocked by an upstream failure.
        foreach (var node in pending)
            results[node.NodeId] = new NodeRunResult(node.NodeId, node.Name, RunStatus.Skipped, NoOutputs, null);

        var success = results.Values.All(r => r.Status == RunStatus.Succeeded);
        await BestEffort(() => _gateway.SetRunStatusAsync(runId, success ? RunStatus.Succeeded : RunStatus.Failed, cancellationToken), "set run status");

        var ordered = dag.Nodes.Select(n => results[n.NodeId]).ToList();
        return new PipelineRunResult(runId, pipelineId, success, ordered, success ? null : "One or more nodes failed.");
    }

    private async Task<NodeRunResult> RunNodeGuardedAsync(
        SemaphoreSlim gate, PipelineDag dag, DagNode node, Guid runId,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, JsonElement>> outputs, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await RunNodeAsync(dag, node, runId, outputs, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<NodeRunResult> RunNodeAsync(
        PipelineDag dag, DagNode node, Guid runId,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, JsonElement>> outputs, CancellationToken cancellationToken)
    {
        var inputs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var wire in dag.WiresInto(node.NodeId))
            if (outputs.TryGetValue(wire.FromNodeId, out var upstream) && upstream.TryGetValue(wire.FromPort, out var value))
                inputs[wire.ToPort] = value;

        var envelope = BuildEnvelope(runId, node, inputs);

        NodeDispatchResult dispatch;
        try
        {
            dispatch = await _gateway.DispatchAsync(node.Subdomain, envelope, cancellationToken);
        }
        catch (Exception ex)
        {
            return Failure(node, $"Dispatch to '{node.Subdomain}' failed: {ex.Message}");
        }

        if (dispatch.StatusCode is < 200 or >= 300)
            return Failure(node, $"Node '{node.Name}' returned HTTP {dispatch.StatusCode}.");

        return ParseNodeResponse(node, dispatch.Body);
    }

    private static NodeRunResult ParseNodeResponse(DagNode node, string body)
    {
        try
        {
            var root = JsonDocument.Parse(body).RootElement;
            var success = root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
            if (!success)
            {
                var error = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String
                    ? e.GetString() : "node reported failure";
                return Failure(node, error ?? "node reported failure");
            }

            var outputs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (root.TryGetProperty("outputs", out var o) && o.ValueKind == JsonValueKind.Object)
                foreach (var p in o.EnumerateObject())
                    outputs[p.Name] = p.Value.Clone();

            return new NodeRunResult(node.NodeId, node.Name, RunStatus.Succeeded, outputs, null);
        }
        catch (JsonException)
        {
            return Failure(node, $"Node '{node.Name}' returned a non-JSON response.");
        }
    }

    private static NodeRunResult Failure(DagNode node, string error) =>
        new(node.NodeId, node.Name, RunStatus.Failed, NoOutputs, error);

    /// <summary>Serialize the node envelope <c>{runId,nodeId,params,inputs}</c>.</summary>
    private static JsonElement BuildEnvelope(Guid runId, DagNode node, IReadOnlyDictionary<string, JsonElement> inputs)
    {
        var json = JsonSerializer.Serialize(new
        {
            runId,
            nodeId = node.NodeId,
            @params = node.Params,
            inputs,
        });
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private async Task BestEffort(Func<Task> action, string what)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Best-effort run persistence step failed: {What}", what);
        }
    }
}
