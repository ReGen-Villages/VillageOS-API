using System.Text.Json;
using System.Text.Json.Nodes;
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

            var batch = await Task.WhenAll(ready.Select(node => RunNodeGuardedAsync(gate, dag, node, rid, outputs, runParams, cancellationToken)));

            foreach (var result in batch)
            {
                results[result.NodeId] = result;
                // Succeeded and partial (#5648 collect-partial) both route outputs downstream; only a hard
                // failure halts dependents.
                if (result.Status is RunStatus.Succeeded or RunStatus.Partial)
                    outputs[result.NodeId] = result.Outputs;
                if (result.Status == RunStatus.Failed)
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

        // A partial node (collect-partial fan-out) does not fail the run; only a hard node failure does.
        var runStatus = cancelled ? RunStatus.Cancelled
            : results.Values.Any(r => r.Status == RunStatus.Failed) ? RunStatus.Failed
            : RunStatus.Succeeded;
        await BestEffort(() => _gateway.SetRunStatusAsync(rid, runStatus, cancellationToken), "set run status");

        var success = runStatus == RunStatus.Succeeded;
        var ordered = dag.Nodes.Select(n => results[n.NodeId]).ToList();

        // The pipeline's published result (#5873): the Output boundary node's collected inputs. Persisted on the
        // PipelineRun Thing (so it streams over SSE and is temporally queryable) and returned to the caller.
        JsonElement? runResult = null;
        var outputNode = dag.Nodes.FirstOrDefault(n => n.Kind == DagNodeKind.Output);
        if (outputNode != null && success)
        {
            var collected = AssembleInputs(dag, outputNode, outputs, runParams);
            runResult = JsonSerializer.SerializeToElement(collected);
            await BestEffort(() => _gateway.SetRunResultAsync(rid, runResult.Value, cancellationToken), "set run result");
        }

        return new PipelineRunResult(rid, pipelineId, success, ordered,
            success ? null : cancelled ? "Run cancelled." : "One or more nodes failed.", runResult);
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
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, JsonElement>> outputs, JsonElement runParams, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await RunNodeAsync(dag, node, runId, outputs, runParams, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<NodeRunResult> RunNodeAsync(
        PipelineDag dag, DagNode node, Guid runId,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, JsonElement>> outputs, JsonElement runParams, CancellationToken cancellationToken)
    {
        // Mark the node (aggregate) running before dispatch so the editor animates it live; the terminal status
        // is written by RunAsync from the value returned here.
        await BestEffort(() => _gateway.SetNodeRunStatusAsync(runId, node.NodeId, node.Name, RunStatus.Running, null, cancellationToken), "persist node running");

        // Boundary nodes (#5873) never dispatch: an Input node projects the run's params onto its output ports
        // (which then flow downstream via wires); an Output node is a sink — its inputs are collected as the
        // run result after the sweep (see RunAsync), so here it simply succeeds.
        if (node.Kind == DagNodeKind.Input)
            return new NodeRunResult(node.NodeId, node.Name, RunStatus.Succeeded, ProjectParamsOntoOutputs(node, runParams), null);
        if (node.Kind == DagNodeKind.Output)
            return new NodeRunResult(node.NodeId, node.Name, RunStatus.Succeeded, NoOutputs, null);

        var inputs = AssembleInputs(dag, node, outputs, runParams);

        // Fan-out (#5648): if the node has a collection input and it carries a list, run the node once per item.
        var collection = node.CollectionInput;
        if (collection != null && inputs.TryGetValue(collection.PortName, out var collValue) && collValue.ValueKind == JsonValueKind.Array)
            return await RunFanOutAsync(node, runId, inputs, collection.PortName, collValue, cancellationToken);

        return await DispatchAndParseAsync(node, runId, inputs, index: null, cancellationToken);
    }

    /// <summary>Assemble a node's inputs: param-bound inputs first (#5647), then wires. Each wire extracts its
    /// from-path of the upstream output and deep-merges it at its to-path into the target input, so several
    /// wires compose one input value; empty paths carry the whole payload and a scalar wire overrides (#5874).</summary>
    private static Dictionary<string, JsonElement> AssembleInputs(
        PipelineDag dag, DagNode node,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, JsonElement>> outputs, JsonElement runParams)
    {
        var accumulated = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var (port, paramKey) in node.ParamBindings)
            if (TryGetParam(runParams, paramKey, out var bound))
                accumulated[port] = JsonSerializer.SerializeToNode(bound);
        foreach (var wire in dag.WiresInto(node.NodeId))
        {
            if (!outputs.TryGetValue(wire.FromNodeId, out var upstream) || !upstream.TryGetValue(wire.FromPort, out var value))
                continue;
            var extracted = PayloadMapping.Extract(value, wire.FromPath);
            if (extracted is null) continue; // the from-path is not present in the upstream output
            var placed = PayloadMapping.Place(wire.ToPath, extracted.Value);
            accumulated[wire.ToPort] = accumulated.TryGetValue(wire.ToPort, out var existing)
                ? PayloadMapping.Merge(existing, placed)
                : placed;
        }

        var inputs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (port, node2) in accumulated)
            inputs[port] = PayloadMapping.ToElement(node2);
        return inputs;
    }

    /// <summary>An Input boundary node's outputs (#5873): each output port is filled from the run param of the
    /// same name, so downstream nodes receive the run's external inputs through ordinary wires.</summary>
    private static IReadOnlyDictionary<string, JsonElement> ProjectParamsOntoOutputs(DagNode node, JsonElement runParams)
    {
        var outputs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var port in node.OutputPorts)
            if (TryGetParam(runParams, port.PortName, out var value))
                outputs[port.PortName] = value;
        return outputs;
    }

    /// <summary>Run the node once per item of its collection input (bounded), writing a per-item NodeRun for each,
    /// and gather each output port into a list. <c>onItemError</c> chooses fail-fast vs collect-partial (#5648).</summary>
    private async Task<NodeRunResult> RunFanOutAsync(
        DagNode node, Guid runId, IReadOnlyDictionary<string, JsonElement> baseInputs,
        string collectionPort, JsonElement items, CancellationToken cancellationToken)
    {
        var itemList = items.EnumerateArray().ToList();
        var total = itemList.Count;
        using var itemGate = new SemaphoreSlim(_maxConcurrency);
        var results = await Task.WhenAll(itemList.Select((item, i) =>
            RunItemAsync(node, runId, baseInputs, collectionPort, item, i, total, itemGate, cancellationToken)));

        var failures = results.Count(r => r.Status != RunStatus.Succeeded);
        var continueOnError = string.Equals(node.OnItemError, ModelNames.OnItemErrorContinue, StringComparison.OrdinalIgnoreCase);

        if (failures > 0 && !continueOnError)
        {
            var firstError = results.First(r => r.Status != RunStatus.Succeeded).Error ?? "an item failed";
            return new NodeRunResult(node.NodeId, node.Name, RunStatus.Failed, NoOutputs, $"Fan-out failed: {firstError}");
        }

        var gathered = GatherOutputs(node, results);
        var status = failures == 0 ? RunStatus.Succeeded
            : failures == total ? RunStatus.Failed
            : RunStatus.Partial;
        return new NodeRunResult(node.NodeId, node.Name, status, gathered, failures > 0 ? $"{failures}/{total} items failed" : null);
    }

    private async Task<NodeRunResult> RunItemAsync(
        DagNode node, Guid runId, IReadOnlyDictionary<string, JsonElement> baseInputs,
        string collectionPort, JsonElement item, int index, int total, SemaphoreSlim itemGate, CancellationToken cancellationToken)
    {
        await itemGate.WaitAsync(cancellationToken);
        try
        {
            await BestEffort(() => _gateway.SetNodeRunStatusAsync(runId, node.NodeId, node.Name, RunStatus.Running, null, cancellationToken, index, total), "persist item running");
            var inputs = new Dictionary<string, JsonElement>(baseInputs, StringComparer.Ordinal) { [collectionPort] = item };
            var result = await DispatchAndParseAsync(node, runId, inputs, index, cancellationToken);
            await BestEffort(() => _gateway.SetNodeRunStatusAsync(runId, node.NodeId, node.Name, result.Status, result.Error, cancellationToken, index, total), "persist item terminal");
            return result;
        }
        finally
        {
            itemGate.Release();
        }
    }

    /// <summary>Gather per-item results into one list per output port (item order; null for a failed item).</summary>
    private static IReadOnlyDictionary<string, JsonElement> GatherOutputs(DagNode node, NodeRunResult[] results)
    {
        var portNames = new HashSet<string>(node.OutputPorts.Select(p => p.PortName), StringComparer.Ordinal);
        foreach (var r in results)
            foreach (var key in r.Outputs.Keys)
                portNames.Add(key);

        var gathered = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var port in portNames)
        {
            var values = results
                .Select(r => r.Status == RunStatus.Succeeded && r.Outputs.TryGetValue(port, out var v) ? v : NullJson)
                .ToArray();
            gathered[port] = JsonSerializer.SerializeToElement(values);
        }
        return gathered;
    }

    private async Task<NodeRunResult> DispatchAndParseAsync(
        DagNode node, Guid runId, IReadOnlyDictionary<string, JsonElement> inputs, int? index, CancellationToken cancellationToken)
    {
        var envelope = BuildEnvelope(runId, node, inputs, index);

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

    private static readonly JsonElement NullJson = JsonSerializer.SerializeToElement<object?>(null);

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

    /// <summary>Look up a run-param by key — the spawn's <c>params</c> object (#5647).</summary>
    private static bool TryGetParam(JsonElement runParams, string key, out JsonElement value)
    {
        if (runParams.ValueKind == JsonValueKind.Object && runParams.TryGetProperty(key, out value))
            return true;
        value = default;
        return false;
    }

    /// <summary>Serialize the node envelope <c>{runId,nodeId,index?,params,inputs}</c>. <c>index</c> is the
    /// fan-out item index (null for a normal single dispatch).</summary>
    private static JsonElement BuildEnvelope(Guid runId, DagNode node, IReadOnlyDictionary<string, JsonElement> inputs, int? index = null)
    {
        var json = JsonSerializer.Serialize(new
        {
            runId,
            nodeId = node.NodeId,
            index,
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
