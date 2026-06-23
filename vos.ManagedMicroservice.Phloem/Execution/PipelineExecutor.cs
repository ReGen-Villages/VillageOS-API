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

    /// <param name="runId">Pre-generated run id for an async spawn (the editor already holds it to animate over
    /// SSE); when null a fresh id is minted (synchronous spawn / graph trigger).</param>
    public async Task<PipelineRunResult> RunAsync(Guid pipelineId, JsonElement runParams, CancellationToken cancellationToken, Guid? runId = null)
    {
        var rid = runId ?? Guid.NewGuid();
        await BestEffort(() => _gateway.CreateRunAsync(rid, pipelineId, cancellationToken), "create run");

        PipelineDag dag;
        try
        {
            var graph = await _gateway.LoadPipelineSubgraphAsync(pipelineId, cancellationToken);
            dag = PipelineDagBuilder.Build(graph, pipelineId, _model);
        }
        catch (PipelineModelException ex)
        {
            return await FailRunAsync(rid, pipelineId, ex.Message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load pipeline {PipelineId}", pipelineId);
            return await FailRunAsync(rid, pipelineId, $"Failed to load pipeline: {ex.Message}", cancellationToken);
        }

        var validation = DagValidator.Validate(dag);
        if (!validation.IsValid)
            return await FailRunAsync(rid, pipelineId, "Pipeline is invalid: " + string.Join(" ", validation.Errors), cancellationToken);

        var outputs = new Dictionary<Guid, IReadOnlyDictionary<string, JsonElement>>();
        var results = new Dictionary<Guid, NodeRunResult>();
        var halted = false;
        var cancelled = false;

        using var gate = new SemaphoreSlim(_maxConcurrency);
        var pending = dag.Nodes.ToList();
        while (pending.Count > 0 && !halted)
        {
            if (await IsCancelledAsync(rid, cancellationToken)) { cancelled = true; break; }

            var ready = pending.Where(n => dag.WiresInto(n.NodeId).All(w => results.ContainsKey(w.FromNodeId))).ToList();
            if (ready.Count == 0) break; // validated DAG => only reachable when an upstream failed

            var batch = await Task.WhenAll(ready.Select(node => RunNodeGuardedAsync(gate, dag, node, rid, outputs, cancellationToken)));

            foreach (var result in batch)
            {
                results[result.NodeId] = result;
                if (result.Status == RunStatus.Succeeded)
                    outputs[result.NodeId] = result.Outputs;
                else
                    halted = true;
                await PersistStatusAsync(rid, result, cancellationToken);
            }
            pending = pending.Where(n => !results.ContainsKey(n.NodeId)).ToList();
        }

        // Anything still pending was blocked by an upstream failure, or skipped because the run was cancelled.
        var pendingStatus = cancelled ? RunStatus.Cancelled : RunStatus.Skipped;
        foreach (var node in pending)
        {
            var result = new NodeRunResult(node.NodeId, node.Name, pendingStatus, NoOutputs, null);
            results[node.NodeId] = result;
            await PersistStatusAsync(rid, result, cancellationToken);
        }

        var runStatus = cancelled ? RunStatus.Cancelled
            : results.Values.All(r => r.Status == RunStatus.Succeeded) ? RunStatus.Succeeded
            : RunStatus.Failed;
        await BestEffort(() => _gateway.SetRunStatusAsync(rid, runStatus, cancellationToken), "set run status");

        var success = runStatus == RunStatus.Succeeded;
        var ordered = dag.Nodes.Select(n => results[n.NodeId]).ToList();
        return new PipelineRunResult(rid, pipelineId, success, ordered,
            success ? null : cancelled ? "Run cancelled." : "One or more nodes failed.");
    }

    private async Task<PipelineRunResult> FailRunAsync(Guid runId, Guid pipelineId, string error, CancellationToken cancellationToken)
    {
        await BestEffort(() => _gateway.SetRunStatusAsync(runId, RunStatus.Failed, cancellationToken), "set run failed");
        return PipelineRunResult.Failed(runId, pipelineId, error);
    }

    private Task PersistStatusAsync(Guid runId, NodeRunResult result, CancellationToken cancellationToken) =>
        BestEffort(() => _gateway.SetNodeRunStatusAsync(runId, result.NodeId, result.Name, result.Status, result.Error, cancellationToken), "persist node run");

    private async Task<bool> IsCancelledAsync(Guid runId, CancellationToken cancellationToken)
    {
        try { return await _gateway.IsCancelRequestedAsync(runId, cancellationToken); }
        catch (Exception ex) { _logger.LogWarning(ex, "Cancel check failed for run {RunId}", runId); return false; }
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
        // Mark the node running before dispatch so the editor animates it live (terminal status follows).
        await BestEffort(() => _gateway.SetNodeRunStatusAsync(runId, node.NodeId, node.Name, RunStatus.Running, null, cancellationToken), "persist node running");

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
