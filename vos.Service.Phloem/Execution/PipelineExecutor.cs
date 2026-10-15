using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using vos.Service.Phloem.Model;
using vos.Service.Shared;

namespace vos.Service.Phloem.Execution;

// Runs a pipeline synchronously (spawn-and-wait): load the subgraph → build the DAG → validate (acyclic +
// port types) → execute nodes in dependency order, dispatching each through Mycelium and routing each
// node's outputs to its downstream inputs → return the full result. Independent nodes in a level run
// concurrently (bounded). Run/NodeRun state is persisted best-effort for the live SSE view.
public sealed class PipelineExecutor
{
    private static readonly IReadOnlyDictionary<string, JsonElement> NoOutputs =
        new Dictionary<string, JsonElement>();

    private readonly IMyceliumGateway _gateway;
    private readonly ILogger<PipelineExecutor> _logger;
    private readonly int _maxConcurrency;
    private readonly ModelClock _clock;
    private readonly RunTurns _turns = new();
    // Held from placing a vigil until the node run is related to it, and by every lookup of a vigil's holder, so
    // an answer arriving at once for a Thing already in the state finds the node run it was placed for.
    private readonly SemaphoreSlim _placing = new(1, 1);

    public PipelineExecutor(
        IMyceliumGateway gateway,
        ILogger<PipelineExecutor> logger,
        ModelClock clock,
        int maxConcurrency = 4)
    {
        _gateway = gateway;
        _logger = logger;
        _maxConcurrency = Math.Max(1, maxConcurrency);
        _clock = clock;
    }

    // runId: Pre-generated run id for an async spawn (the editor already holds it to animate over
    // SSE); when null a fresh id is minted (synchronous spawn / dispatched relationship).
    // subject: the Thing whose state entry or relationship started the run. It is recorded on the run and
    // reaches the pipeline as the run param `subject` — the name the page gives a start node's port — as
    // one value carrying the Thing's id and name, so a wire narrows it to either by its from-path.
    public async Task<PipelineRunResult> RunAsync(
        Guid pipelineId, JsonElement runParams, CancellationToken cancellationToken, Guid? runId = null, RunSubject? subject = null,
        Guid? requestId = null)
    {
        var rid = runId ?? Guid.NewGuid();
        if (subject is not null) runParams = WithSubject(runParams, subject);
        await BestEffort(() => _gateway.CreateRunAsync(rid, pipelineId, cancellationToken, subject, requestId, runParams), "create run");

        var (dag, refusal) = await LoadAsync(pipelineId, cancellationToken);
        if (dag is null) return await FailRunAsync(rid, pipelineId, refusal!, cancellationToken);

        using var turn = await _turns.TakeAsync(rid, cancellationToken);
        return await SweepAsync(rid, dag, runParams, new Progress(), cancellationToken);
    }

    // The vigil's answer: the node run holding it succeeds with the outputs its service answered before the wait,
    // and the run carries on from its record. Nothing is dispatched a second time.
    public async Task<ResumeOutcome> ResumeAsync(Guid vigilId, CancellationToken cancellationToken)
    {
        if (await HolderOfAsync(vigilId, cancellationToken) is not { } holder) return ResumeOutcome.NotHeld;

        using var turn = await _turns.TakeAsync(holder.RunId, cancellationToken);
        var record = await _gateway.ReadRunRecordAsync(holder.RunId, cancellationToken);
        if (record is null || !record.Nodes.TryGetValue(holder.NodeId, out var held) || held.Status != RunStatus.Waiting)
            return ResumeOutcome.AlreadyResumed;

        var (dag, refusal) = await LoadAsync(record.PipelineId, cancellationToken);
        if (dag is null)
        {
            await FailRunAsync(record.RunId, record.PipelineId, refusal!, cancellationToken);
            return ResumeOutcome.Resumed;
        }

        var (progress, missing) = ProgressFrom(dag, record);
        if (missing is not null)
        {
            await FailRunAsync(record.RunId, record.PipelineId,
                $"The record of '{missing.Name}' is missing, so the run cannot resume without running it again.", cancellationToken);
            return ResumeOutcome.Resumed;
        }

        var resumed = progress.Waiting[holder.NodeId] with { Status = RunStatus.Succeeded, Awaits = null };
        progress.Waiting.Remove(holder.NodeId);
        progress.Results[holder.NodeId] = resumed;
        progress.Outputs[holder.NodeId] = resumed.Outputs;
        await PersistStatusAsync(record.RunId, resumed, cancellationToken);
        await BestEffort(() => _gateway.ForgetAwaitedAsync(record.RunId, holder.NodeId, cancellationToken), "forget the awaited Thing");
        await BestEffort(() => _gateway.SetRunStatusAsync(record.RunId, RunStatus.Running, cancellationToken), "set run running");

        await SweepAsync(record.RunId, dag, record.Parameters, progress, cancellationToken);
        return ResumeOutcome.Resumed;
    }

    public async Task<bool> HoldsVigilAsync(Guid vigilId, CancellationToken cancellationToken) =>
        await HolderOfAsync(vigilId, cancellationToken) is not null;

    private async Task<VigilHolder?> HolderOfAsync(Guid vigilId, CancellationToken cancellationToken)
    {
        await _placing.WaitAsync(cancellationToken);
        try { return await _gateway.HolderOfVigilAsync(vigilId, cancellationToken); }
        finally { _placing.Release(); }
    }

    // On start: every waiting node run whose vigil is gone gets one again, since a vigil answered while this
    // orchestrator was not there to resume the run is retired with nothing resumed. Answers how many were placed.
    public async Task<int> RecoverWaitsAsync(CancellationToken cancellationToken)
    {
        var placed = 0;
        foreach (var wait in await _gateway.WaitingNodeRunsAsync(cancellationToken))
        {
            if (wait.VigilId is { } vigil && await _gateway.IsVigilLiveAsync(vigil, cancellationToken)) continue;

            using var turn = await _turns.TakeAsync(wait.RunId, cancellationToken);
            var record = await _gateway.ReadRunRecordAsync(wait.RunId, cancellationToken);
            if (record is null) continue;
            var (dag, refusal) = await LoadAsync(record.PipelineId, cancellationToken);
            var node = dag?.Node(wait.NodeId);
            var name = node?.Name ?? wait.NodeId.ToString();

            string? failure = null;
            if (wait.AwaitedId is not { } awaited)
                failure = $"'{name}' waited for a Thing the model no longer names, so nothing could resume it.";
            else if (dag is null)
                failure = refusal;
            else if (node?.Waits is not { } waits)
                failure = $"'{name}' no longer waits for a state, so nothing could resume it.";
            else if (await PlaceAsync(record.RunId, node.NodeId, awaited, waits, cancellationToken) is { } refused)
                failure = $"'{name}' cannot wait: {refused}";
            else
                placed++;

            if (failure is null) continue;
            await BestEffort(() => _gateway.SetNodeRunStatusAsync(record.RunId, wait.NodeId, name, RunStatus.Failed, failure, cancellationToken), "fail the wait");
            await FailRunAsync(record.RunId, record.PipelineId, failure, cancellationToken);
        }
        return placed;
    }

    private async Task<(PipelineDag? Dag, string? Refusal)> LoadAsync(Guid pipelineId, CancellationToken cancellationToken)
    {
        PipelineDag dag;
        try
        {
            var graph = await _gateway.LoadPipelineSubgraphAsync(pipelineId, cancellationToken);
            dag = PipelineDagBuilder.Build(graph, pipelineId);
        }
        catch (PipelineModelException ex)
        {
            return (null, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load pipeline {PipelineId}", pipelineId);
            return (null, $"Failed to load pipeline: {ex.Message}");
        }

        var validation = DagValidator.Validate(dag);
        return validation.IsValid ? (dag, null) : (null, "Pipeline is invalid: " + string.Join(" ", validation.Errors));
    }

    // What a sweep has settled so far: a fresh run starts with nothing, a resumed one with its record.
    private sealed class Progress
    {
        public Dictionary<Guid, NodeRunResult> Results { get; } = new();
        public Dictionary<Guid, IReadOnlyDictionary<string, JsonElement>> Outputs { get; } = new();
        public Dictionary<Guid, NodeRunResult> Waiting { get; } = new();
        // Waiting nodes that already hold a vigil from an earlier sweep.
        public HashSet<Guid> Holding { get; } = new();
    }

    // A node with no record is one the run had not reached, which only a node behind a wait can be; any other
    // missing record means the run would have to dispatch that node again to carry on.
    private static (Progress Progress, DagNode? Missing) ProgressFrom(PipelineDag dag, RunRecord record)
    {
        var progress = new Progress();
        foreach (var node in dag.Nodes)
        {
            if (!record.Nodes.TryGetValue(node.NodeId, out var recorded) || recorded.Status == RunStatus.Running) continue;
            var result = new NodeRunResult(node.NodeId, node.Name, recorded.Status, recorded.Outputs, null);
            if (recorded.Status == RunStatus.Waiting)
            {
                progress.Waiting[node.NodeId] = result;
                progress.Holding.Add(node.NodeId);
                continue;
            }
            progress.Results[node.NodeId] = result;
            if (recorded.Status is RunStatus.Succeeded or RunStatus.Partial)
                progress.Outputs[node.NodeId] = recorded.Outputs;
        }

        var behindAWait = Downstream(dag, progress.Waiting.Keys);
        var missing = dag.Nodes.FirstOrDefault(node => !progress.Results.ContainsKey(node.NodeId)
            && !progress.Waiting.ContainsKey(node.NodeId) && !behindAWait.Contains(node.NodeId));
        return (progress, missing);
    }

    private static HashSet<Guid> Downstream(PipelineDag dag, IEnumerable<Guid> from)
    {
        var reached = new HashSet<Guid>();
        var frontier = new Stack<Guid>(from);
        while (frontier.Count > 0)
        {
            var node = frontier.Pop();
            foreach (var wire in dag.Wires.Where(wire => wire.FromNodeId == node))
                if (reached.Add(wire.ToNodeId))
                    frontier.Push(wire.ToNodeId);
        }
        return reached;
    }

    private async Task<PipelineRunResult> SweepAsync(
        Guid rid, PipelineDag dag, JsonElement runParams, Progress progress, CancellationToken cancellationToken)
    {
        var pipelineId = dag.PipelineId;
        var results = progress.Results;
        var outputs = progress.Outputs;
        var halted = false;
        var cancelled = false;

        bool Unsettled(DagNode node) => !results.ContainsKey(node.NodeId) && !progress.Waiting.ContainsKey(node.NodeId);

        using var gate = new SemaphoreSlim(_maxConcurrency);
        var pending = dag.Nodes.Where(Unsettled).ToList();
        while (pending.Count > 0 && !halted)
        {
            if (await IsCancelledAsync(rid, cancellationToken)) { cancelled = true; break; }

            // A node behind a waiting one is not ready until the wait is over.
            var ready = pending.Where(n => dag.WiresInto(n.NodeId).All(w => results.ContainsKey(w.FromNodeId))).ToList();
            if (ready.Count == 0) break;

            // A node wired only from outcomes that were not answered is the branch not taken: it is recorded
            // and nothing is dispatched to it, so a node behind the alternatives runs once, from the one that
            // carried.
            var notTaken = ready.Where(n => dag.WiresInto(n.NodeId).Any() && !dag.WiresInto(n.NodeId).Any(w => Carried(w, outputs))).ToList();
            foreach (var node in notTaken)
            {
                var result = new NodeRunResult(node.NodeId, node.Name, RunStatus.NotTaken, NoOutputs, null);
                results[node.NodeId] = result;
                await PersistStatusAsync(rid, result, cancellationToken);
            }

            var batch = await Task.WhenAll(ready.Except(notTaken).Select(node => RunNodeGuardedAsync(gate, dag, node, rid, outputs, runParams, cancellationToken)));

            foreach (var result in batch)
            {
                if (result.Status == RunStatus.Waiting)
                    progress.Waiting[result.NodeId] = result;
                else
                    results[result.NodeId] = result;
                // Succeeded and partial (collect-partial) both route outputs downstream; only a hard
                // failure halts dependents.
                if (result.Status is RunStatus.Succeeded or RunStatus.Partial)
                    outputs[result.NodeId] = result.Outputs;
                if (result.Status == RunStatus.Failed)
                    halted = true;
                await PersistStatusAsync(rid, result, cancellationToken);
            }
            pending = pending.Where(Unsettled).ToList();
        }

        if (!halted && !cancelled && progress.Waiting.Count > 0)
        {
            var failures = await HoldAsync(rid, dag, progress, cancellationToken);
            foreach (var failure in failures)
                await PersistStatusAsync(rid, failure, cancellationToken);
            if (failures.Count == 0)
            {
                await BestEffort(() => _gateway.SetRunStatusAsync(rid, RunStatus.Waiting, cancellationToken), "set run waiting");
                var waits = progress.Waiting.Values.Select(wait =>
                    $"The run waits for {(wait.Awaits is { } thing ? $"'{thing.Name}'" : "its Thing")} to enter '{dag.Node(wait.NodeId)!.Waits!.RangeName}' at '{wait.Name}'.");
                return new PipelineRunResult(rid, pipelineId, false,
                    dag.Nodes.Where(node => !Unsettled(node))
                        .Select(node => results.TryGetValue(node.NodeId, out var settled) ? settled : progress.Waiting[node.NodeId]).ToList(),
                    string.Join(" ", waits));
            }
            halted = true;
        }

        // Anything still pending, or waiting, was blocked by an upstream failure, or skipped because the run was cancelled.
        var pendingStatus = cancelled ? RunStatus.Cancelled : RunStatus.Skipped;
        foreach (var node in pending.Concat(progress.Waiting.Keys.Select(id => dag.Node(id)!)))
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

        // The pipeline's published result: the Output boundary node's collected inputs. Persisted on the
        // PipelineRun Thing (so it streams over SSE and is temporally queryable) and returned to the caller.
        JsonElement? runResult = null;
        var outputNode = dag.Nodes.FirstOrDefault(n => n.Kind == DagNodeKind.Output);
        if (outputNode != null && success && results[outputNode.NodeId].Status == RunStatus.Succeeded)
        {
            try
            {
                var collected = AssembleInputs(dag, outputNode, outputs, runParams);
                runResult = JsonSerializer.SerializeToElement(collected);
                await BestEffort(() => _gateway.SetRunResultAsync(rid, runResult.Value, cancellationToken), "set run result");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to assemble the Output node result for run {RunId}", rid);
            }
        }

        return new PipelineRunResult(rid, pipelineId, success, ordered,
            success ? null : cancelled ? "Run cancelled." : "One or more nodes failed.", runResult);
    }

    private async Task<PipelineRunResult> FailRunAsync(Guid runId, Guid pipelineId, string error, CancellationToken cancellationToken)
    {
        await BestEffort(() => _gateway.SetRunStatusAsync(runId, RunStatus.Failed, cancellationToken, error), "set run failed");
        return PipelineRunResult.Failed(runId, pipelineId, error);
    }

    // A resumed run reads every settled node back from the model, so the record is made whole before any vigil is
    // placed. A node whose record still cannot be written fails, and the run with it, rather than waiting on a
    // record it could not resume from. Answers the nodes that failed, none when every wait is held.
    private async Task<IReadOnlyList<NodeRunResult>> HoldAsync(Guid runId, PipelineDag dag, Progress progress, CancellationToken cancellationToken)
    {
        var settled = progress.Results.Values.Concat(progress.Waiting.Values).ToList();
        foreach (var unwritten in await UnrecordedAsync(runId, settled, cancellationToken))
            await BestEffort(() => _gateway.SetNodeRunStatusAsync(runId, unwritten.NodeId, unwritten.Name, unwritten.Status, unwritten.Error,
                cancellationToken, outputs: unwritten.Outputs), "write a missing record again");

        var failures = new List<NodeRunResult>();
        foreach (var unwritten in await UnrecordedAsync(runId, settled, cancellationToken))
            FailHere(unwritten, $"The record of '{unwritten.Name}' could not be written to the model, so the run could not resume from it.");
        if (failures.Count > 0) return failures;

        foreach (var wait in progress.Waiting.Values.Where(wait => !progress.Holding.Contains(wait.NodeId)).ToList())
        {
            if (await PlaceAsync(runId, wait.NodeId, wait.Awaits!.Id, dag.Node(wait.NodeId)!.Waits!, cancellationToken) is { } refused)
                FailHere(wait, $"'{wait.Name}' cannot wait: {refused}");
            else
                progress.Holding.Add(wait.NodeId);
        }
        return failures;

        void FailHere(NodeRunResult node, string reason)
        {
            var failed = node with { Status = RunStatus.Failed, Error = reason, Outputs = NoOutputs };
            progress.Waiting.Remove(node.NodeId);
            progress.Results[node.NodeId] = failed;
            failures.Add(failed);
        }
    }

    private async Task<IReadOnlyList<NodeRunResult>> UnrecordedAsync(Guid runId, IReadOnlyList<NodeRunResult> settled, CancellationToken cancellationToken)
    {
        RunRecord? record = null;
        try { record = await _gateway.ReadRunRecordAsync(runId, cancellationToken); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not read run {RunId} back", runId); }
        return settled.Where(node => record is null
            || !record.Nodes.TryGetValue(node.NodeId, out var recorded) || recorded.Status != node.Status).ToList();
    }

    // The node run is related to what it waits for before the vigil exists, and to the vigil once it does. Answers
    // why the vigil could not be placed, or null when it was.
    private async Task<string?> PlaceAsync(Guid runId, Guid nodeId, Guid awaitedId, DagWait wait, CancellationToken cancellationToken)
    {
        try
        {
            await _gateway.RelateAwaitedAsync(runId, nodeId, awaitedId, cancellationToken);
            await _placing.WaitAsync(cancellationToken);
            try
            {
                switch (await _gateway.PlaceVigilAsync(awaitedId, wait.RangeId, cancellationToken))
                {
                    case VigilPlacement.Refused refused:
                        return $"the platform refused its vigil: {refused.Reason}";
                    case VigilPlacement.Placed placed:
                        await _gateway.RelateHeldVigilAsync(runId, nodeId, placed.VigilId, cancellationToken);
                        return null;
                    default:
                        return "the platform placed no vigil.";
                }
            }
            finally
            {
                _placing.Release();
            }
        }
        catch (Exception ex)
        {
            return $"its vigil could not be placed: {ex.Message}";
        }
    }

    private Task PersistStatusAsync(Guid runId, NodeRunResult result, CancellationToken cancellationToken) =>
        BestEffort(() => _gateway.SetNodeRunStatusAsync(runId, result.NodeId, result.Name, result.Status, result.Error, cancellationToken, outputs: result.Outputs), "persist node run");

    // Each Thing answered under an out port marked as producing one: a value carrying an `id`, an identifier
    // on its own, or a list of either.
    private Task RecordProducedAsync(Guid runId, DagNode node, int? index, NodeRunResult result, CancellationToken cancellationToken)
    {
        var produced = new List<Guid>();
        foreach (var port in node.OutputPorts)
            if (port.Produces && result.Outputs.TryGetValue(port.PortName, out var answered))
                produced.AddRange(ThingIdentifiersIn(answered));
        return produced.Count == 0
            ? Task.CompletedTask
            : BestEffort(() => _gateway.RelateProducedAsync(runId, node.NodeId, index, produced, cancellationToken), "relate produced Things");
    }

    private static IEnumerable<Guid> ThingIdentifiersIn(JsonElement answered)
    {
        switch (answered.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in answered.EnumerateArray())
                    foreach (var id in ThingIdentifiersIn(item)) yield return id;
                break;
            case JsonValueKind.Object when answered.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && Guid.TryParse(id.GetString(), out var parsed):
                yield return parsed;
                break;
            case JsonValueKind.String when Guid.TryParse(answered.GetString(), out var bare):
                yield return bare;
                break;
        }
    }

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

    // Sending is writing: the broker delivers the message once from the model, so a failure here is a write the
    // broker refused, never a message half sent.
    private async Task<NodeRunResult> TellAsync(
        PipelineDag dag, DagNode node, Telling telling, Guid runId,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, JsonElement>> outputs, JsonElement runParams, CancellationToken cancellationToken)
    {
        try
        {
            var payload = JsonSerializer.SerializeToElement(AssembleInputs(dag, node, outputs, runParams));
            await _gateway.SendMessageAsync(runId, telling, payload, cancellationToken);
            return new NodeRunResult(node.NodeId, node.Name, RunStatus.Succeeded, NoOutputs, null);
        }
        catch (Exception ex)
        {
            return new NodeRunResult(node.NodeId, node.Name, RunStatus.Failed, NoOutputs,
                $"The message to '{telling.SystemName}' could not be written: {ex.Message}");
        }
    }

    private async Task<NodeRunResult> RunNodeAsync(
        PipelineDag dag, DagNode node, Guid runId,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, JsonElement>> outputs, JsonElement runParams, CancellationToken cancellationToken)
    {
        // Mark the node (aggregate) running before dispatch so the editor animates it live; the terminal status
        // is written by RunAsync from the value returned here.
        await BestEffort(() => _gateway.SetNodeRunStatusAsync(runId, node.NodeId, node.Name, RunStatus.Running, null, cancellationToken), "persist node running");

        // Boundary nodes never dispatch: an Input node projects the run's params onto its output ports
        // (which then flow downstream via wires); an Output node is a sink — its inputs are collected as the
        // run result after the sweep (see RunAsync), so here it simply succeeds.
        if (node.Kind == DagNodeKind.Input)
            return new NodeRunResult(node.NodeId, node.Name, RunStatus.Succeeded, ProjectParamsOntoOutputs(node, runParams), null);
        if (node.Kind == DagNodeKind.Output)
            return node.Tells is null
                ? new NodeRunResult(node.NodeId, node.Name, RunStatus.Succeeded, NoOutputs, null)
                : await TellAsync(dag, node, node.Tells, runId, outputs, runParams, cancellationToken);

        Dictionary<string, JsonElement> inputs;
        try
        {
            inputs = AssembleInputs(dag, node, outputs, runParams);
        }
        catch (Exception ex)
        {
            return new NodeRunResult(node.NodeId, node.Name, RunStatus.Failed, NoOutputs, $"Wire transform failed: {ex.Message}");
        }

        // Fan-out: if the node has a collection input and it carries a list, run the node once per item.
        var collection = node.CollectionInput;
        if (collection != null && inputs.TryGetValue(collection.PortName, out var collValue) && collValue.ValueKind == JsonValueKind.Array)
            return await RunFanOutAsync(node, runId, inputs, collection.PortName, collValue, cancellationToken);

        AwaitedThing? awaited = null;
        if (node.Waits is { } wait)
        {
            awaited = inputs.TryGetValue(wait.PortName, out var reaching) ? ThingNamedBy(reaching) : null;
            if (awaited is null)
                return Failure(node, $"'{node.Name}' waits for the Thing reaching its port '{wait.PortName}', but what reached it names no Thing.");
        }

        var result = await DispatchAndParseAsync(node, runId, inputs, index: null, cancellationToken);
        await RecordProducedAsync(runId, node, index: null, result, cancellationToken);
        return awaited is not null && result.Status == RunStatus.Succeeded
            ? result with { Status = RunStatus.Waiting, Awaits = awaited }
            : result;
    }

    // A Thing as a run carries it: a value with an `id` and usually a `name`, or an identifier on its own.
    private static AwaitedThing? ThingNamedBy(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object when value.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                                  && Guid.TryParse(id.GetString(), out var parsed) =>
            new AwaitedThing(parsed, value.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                ? name.GetString()!
                : parsed.ToString()),
        JsonValueKind.String when Guid.TryParse(value.GetString(), out var bare) => new AwaitedThing(bare, bare.ToString()),
        _ => null,
    };

    private static bool Carried(DagWire wire, IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, JsonElement>> outputs) =>
        outputs.TryGetValue(wire.FromNodeId, out var upstream) && upstream.ContainsKey(wire.FromPort);

    // Assemble a node's inputs: the Thing chosen on the node, then param-bound inputs, then wires. Each
    // wire extracts its from-path of the upstream output and deep-merges it at its to-path into the target
    // input, so several wires compose one input value; empty paths carry the whole payload and a scalar
    // wire overrides. Wires into a collection input are gathered into one list instead, in wire order.
    private Dictionary<string, JsonElement> AssembleInputs(
        PipelineDag dag, DagNode node,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, JsonElement>> outputs, JsonElement runParams)
    {
        var accumulated = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var (port, chosen) in node.Choices)
            accumulated[port] = new JsonObject { ["id"] = chosen.Id.ToString(), ["name"] = chosen.Name };
        foreach (var (port, paramKey) in node.ParameterBindings)
            if (TryGetParam(runParams, paramKey, out var bound))
                accumulated[port] = JsonSerializer.SerializeToNode(bound);
        foreach (var wire in dag.WiresInto(node.NodeId))
        {
            if (!outputs.TryGetValue(wire.FromNodeId, out var upstream) || !upstream.TryGetValue(wire.FromPort, out var value))
                continue;
            var extracted = PayloadMapping.Extract(value, wire.FromPath);
            if (extracted is null) continue; // the from-path is not present in the upstream output
            var reshaped = string.IsNullOrEmpty(wire.Transform)
                ? extracted.Value
                : new JsonataTransform(wire.Transform).Eval(extracted.Value, _clock);
            if (node.Port(wire.ToPort) is { IsInput: true, Collection: true })
            {
                var gathered = accumulated.TryGetValue(wire.ToPort, out var soFar) ? AsList(soFar) : new JsonArray();
                AppendItems(gathered, reshaped);
                accumulated[wire.ToPort] = gathered;
                continue;
            }
            var placed = PayloadMapping.Place(wire.ToPath, reshaped);
            accumulated[wire.ToPort] = accumulated.TryGetValue(wire.ToPort, out var existing)
                ? PayloadMapping.Merge(existing, placed)
                : placed;
        }

        var inputs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (port, node2) in accumulated)
            inputs[port] = PayloadMapping.ToElement(node2);
        return inputs;
    }

    // The subject is the broker's word on what entered, so it wins over a relationship property of the
    // same name.
    private static JsonElement WithSubject(JsonElement runParams, RunSubject subject)
    {
        var merged = runParams.ValueKind == JsonValueKind.Object
            ? JsonSerializer.SerializeToNode(runParams)!.AsObject()
            : new JsonObject();
        merged[SubjectParam] = new JsonObject { ["id"] = subject.Id.ToString(), ["name"] = subject.Name };
        return JsonSerializer.SerializeToElement(merged);
    }

    public const string SubjectParam = "subject";

    // An Input boundary node's outputs: each output port is filled from the run param of the
    // same name, so downstream nodes receive the run's external inputs through ordinary wires.
    private static IReadOnlyDictionary<string, JsonElement> ProjectParamsOntoOutputs(DagNode node, JsonElement runParams)
    {
        var outputs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var port in node.OutputPorts)
            if (TryGetParam(runParams, port.PortName, out var value))
                outputs[port.PortName] = value;
        return outputs;
    }

    // Run the node once per item of its collection input (bounded), writing a per-item NodeRun for each,
    // and gather each output port into a list. onItemError chooses fail-fast vs collect-partial.
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
            await BestEffort(() => _gateway.SetNodeRunStatusAsync(runId, node.NodeId, node.Name, result.Status, result.Error, cancellationToken, index, total, result.Outputs), "persist item terminal");
            await RecordProducedAsync(runId, node, index, result, cancellationToken);
            return result;
        }
        finally
        {
            itemGate.Release();
        }
    }

    // Gather per-item results into one list per output port (item order; null for a failed item). An out
    // port marked as a collection gathers flat: every item's list is concatenated, a failed item adds nothing,
    // so a fan-out nested however deep still answers one list of items.
    private static IReadOnlyDictionary<string, JsonElement> GatherOutputs(DagNode node, NodeRunResult[] results)
    {
        var portNames = new HashSet<string>(node.OutputPorts.Select(p => p.PortName), StringComparer.Ordinal);
        foreach (var r in results)
            foreach (var key in r.Outputs.Keys)
                portNames.Add(key);

        var gathered = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var port in portNames)
        {
            if (node.Port(port) is { IsOutput: true, Collection: true })
            {
                var flat = new JsonArray();
                foreach (var r in results)
                    if (r.Status == RunStatus.Succeeded && r.Outputs.TryGetValue(port, out var answered))
                        AppendItems(flat, answered);
                gathered[port] = PayloadMapping.ToElement(flat);
                continue;
            }
            var values = results
                .Select(r => r.Status == RunStatus.Succeeded && r.Outputs.TryGetValue(port, out var v) ? v : NullJson)
                .ToArray();
            gathered[port] = JsonSerializer.SerializeToElement(values);
        }
        return gathered;
    }

    private static void AppendItems(JsonArray list, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) list.Add(JsonSerializer.SerializeToNode(item));
        else
            list.Add(JsonSerializer.SerializeToNode(value));
    }

    // A value already on a collection input, bound from a parameter or chosen on the node, is the first item
    // unless it is a list already.
    private static JsonArray AsList(JsonNode? value) =>
        value is JsonArray list ? list : new JsonArray { value?.DeepClone() };

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

    private static bool TryGetParam(JsonElement runParams, string key, out JsonElement value)
    {
        if (runParams.ValueKind == JsonValueKind.Object && runParams.TryGetProperty(key, out value))
            return true;
        value = default;
        return false;
    }

    // Serialize the node envelope {runId,nodeId,index?,params,inputs}. index is the
    // fan-out item index (null for a normal single dispatch).
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
