using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using vos.Service.Phloem.Model;
using vos.Service.Shared;

namespace vos.Service.Phloem.Execution;

// Runs a pipeline synchronously (spawn-and-wait): load the subgraph → build the DAG → validate (acyclic +
// port types) → execute nodes in dependency order, calling each through Mycelium and routing each
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
    // A stop asked for a run whose sweep is under way here, read between its steps and by a pause between attempts.
    // Held only while the sweep or the stop that set it is working, so nothing outlives a run.
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<StopRequested>> _stops = new();

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
    // SSE); when null a fresh id is minted (synchronous spawn / a relationship the broker sends).
    // subject: the Thing whose state entry or relationship started the run. It is recorded on the run and
    // reaches the pipeline as the run param `subject` — the name the page gives a start node's port — as
    // one value carrying the Thing's id and name, so a wire narrows it to either by its from-path.
    public async Task<PipelineRunResult> RunAsync(
        Guid pipelineId, JsonElement runParams, CancellationToken cancellationToken, Guid? runId = null, RunSubject? subject = null,
        Guid? requestId = null, Guid? startedBy = null, bool rehearsal = false)
    {
        var rid = runId ?? Guid.NewGuid();
        if (subject is not null) runParams = WithSubject(runParams, subject);

        // Held from before the run exists, so a stop finds either no run or one whose sweep sees the stop.
        using var turn = await _turns.TakeAsync(rid, cancellationToken);
        var (dag, refusal) = await LoadAsync(pipelineId, cancellationToken, new Start(runParams, rehearsal));
        try
        {
            await _gateway.CreateRunAsync(rid, pipelineId, cancellationToken, subject, requestId, runParams, startedBy, refusal, rehearsal);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _logger.LogError(error, "Run {RunId} of pipeline {PipelineId} could not be recorded, so it does not start", rid, pipelineId);
            return PipelineRunResult.Failed(rid, pipelineId, refusal ?? error.Message);
        }
        if (dag is null) return PipelineRunResult.Failed(rid, pipelineId, refusal!);

        return await SweepAsync(rid, dag, runParams, new Progress(), rehearsal, cancellationToken);
    }

    public Task<bool> IsStopAsync(Guid relationshipId, CancellationToken cancellationToken) =>
        _gateway.IsStopAsync(relationshipId, cancellationToken);

    // A run whose sweep is under way here stops at its next step, or at once from a pause between attempts, and its
    // sweep records the stop. A run waiting for a state, or left running by an orchestrator that stopped, has no sweep
    // to see it, so it is ended here under its turn: its vigils retired, each node not finished recorded stopped.
    // Every run is told before any turn is waited for, and each is ended on its own, so a run in the middle of a
    // long call holds up none of the others.
    public async Task StopAsync(Guid stopRequestId, Guid targetId, CancellationToken cancellationToken)
    {
        var request = await _gateway.StopRequestedAsync(stopRequestId, targetId, cancellationToken);
        foreach (var runId in request.Runs)
            _stops.GetOrAdd(runId, _ => NewStop()).TrySetResult(request);
        await Task.WhenAll(request.Runs.Select(runId => EndUnlessItsSweepDoesAsync(runId, request, cancellationToken)));
    }

    private async Task EndUnlessItsSweepDoesAsync(Guid runId, StopRequested request, CancellationToken cancellationToken)
    {
        try
        {
            using var turn = await _turns.TakeAsync(runId, cancellationToken);
            if (await _gateway.ReadRunRecordAsync(runId, cancellationToken) is { } record && !RunStatus.IsFinished(record.Status))
                await EndStoppedRunAsync(record, request, cancellationToken);
        }
        finally
        {
            _stops.TryRemove(runId, out _);
        }
    }

    private async Task EndStoppedRunAsync(RunRecord record, StopRequested request, CancellationToken cancellationToken)
    {
        var (dag, _) = await LoadAsync(record.PipelineId, cancellationToken);
        var waits = record.Nodes.Values.Any(node => node.Status == RunStatus.Waiting)
            ? (await _gateway.WaitingNodeRunsAsync(cancellationToken)).Where(wait => wait.RunId == record.RunId).ToList()
            : [];
        foreach (var wait in waits)
        {
            if (wait.VigilId is { } vigil) await BestEffort(() => _gateway.RetireVigilAsync(vigil, cancellationToken), "retire the vigil");
            await BestEffort(() => _gateway.ForgetAwaitedAsync(record.RunId, wait.NodeId, cancellationToken), "forget the awaited Thing");
        }

        foreach (var node in dag?.Nodes ?? [])
        {
            if (record.Nodes.TryGetValue(node.NodeId, out var recorded) && RunStatus.IsFinished(recorded.Status)) continue;
            await PersistStatusAsync(record.RunId, new NodeRunResult(node.NodeId, node.Name, RunStatus.Cancelled, NoOutputs, request.Reason), cancellationToken);
        }
        await BestEffort(() => _gateway.SetRunStatusAsync(record.RunId, RunStatus.Cancelled, cancellationToken, request.Reason), "set run stopped");
        await BestEffort(() => _gateway.RelateStoppedByAsync(record.RunId, request.RequestId, cancellationToken), "relate the stop");
    }

    private static TaskCompletionSource<StopRequested> NewStop() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private StopRequested? StopOf(Guid runId) =>
        _stops.TryGetValue(runId, out var stop) && stop.Task.IsCompletedSuccessfully ? stop.Task.Result : null;

    // What a run is judged against when it starts, beside its drawing.
    private sealed record Start(JsonElement Parameters, bool Rehearsal);

    // The vigil's answer: the node run holding it succeeds with the outputs its service answered before the wait,
    // and the run carries on from its record. Nothing is called a second time.
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
        progress.Settle(dag, resumed);
        await PersistStatusAsync(record.RunId, resumed, cancellationToken);
        await BestEffort(() => _gateway.ForgetAwaitedAsync(record.RunId, holder.NodeId, cancellationToken), "forget the awaited Thing");
        await BestEffort(() => _gateway.SetRunStatusAsync(record.RunId, RunStatus.Running, cancellationToken), "set run running");

        await SweepAsync(record.RunId, dag, record.Parameters, progress, record.Rehearsal, cancellationToken);
        return ResumeOutcome.Resumed;
    }

    public async Task<bool> HoldsVigilAsync(Guid vigilId, AnswerTimings timings, CancellationToken cancellationToken) =>
        await HolderOfAsync(vigilId, cancellationToken, timings) is not null;

    private async Task<VigilHolder?> HolderOfAsync(Guid vigilId, CancellationToken cancellationToken, AnswerTimings? timings = null)
    {
        var waiting = Stopwatch.StartNew();
        await _placing.WaitAsync(cancellationToken);
        timings?.Record("waiting for the placing lock", waiting.ElapsedMilliseconds);
        var reading = Stopwatch.StartNew();
        try { return await _gateway.HolderOfVigilAsync(vigilId, cancellationToken); }
        finally
        {
            timings?.Record("reading the vigil's holder", reading.ElapsedMilliseconds);
            _placing.Release();
        }
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

    // On start: a node run left mid-trying carries on from its record, its next attempt numbered after the ones made.
    // A fan-out's item cannot: the sweep runs the whole node again, so the run fails rather than calling its items twice.
    // Answers how many runs were carried on.
    public async Task<int> RecoverTryingAsync(CancellationToken cancellationToken)
    {
        var carriedOn = 0;
        var seen = new HashSet<Guid>();
        foreach (var trying in await _gateway.TryingNodeRunsAsync(cancellationToken))
        {
            if (!seen.Add(trying.RunId)) continue;
            using var turn = await _turns.TakeAsync(trying.RunId, cancellationToken);
            var record = await _gateway.ReadRunRecordAsync(trying.RunId, cancellationToken);
            if (record is null) continue;
            var (dag, refusal) = await LoadAsync(record.PipelineId, cancellationToken);
            var name = dag?.Node(trying.NodeId)?.Name ?? trying.NodeId.ToString();
            var (progress, missing) = dag is null ? (null, null) : ProgressFrom(dag, record);

            var failure = dag is null ? refusal
                : trying.PerItem ? $"'{name}' was trying one of its items when the orchestrator stopped, and a fan-out cannot be carried on from its record."
                : missing is not null ? $"The record of '{missing.Name}' is missing, so the run cannot be carried on without running it again."
                : null;

            if (failure is not null)
            {
                await BestEffort(() => _gateway.SetNodeRunStatusAsync(record.RunId, trying.NodeId, name, RunStatus.Failed, failure, cancellationToken), "fail the trying node");
                await FailRunAsync(record.RunId, record.PipelineId, failure, cancellationToken);
                continue;
            }

            carriedOn++;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var ownTurn = await _turns.TakeAsync(record.RunId, CancellationToken.None);
                    await SweepAsync(record.RunId, dag!, record.Parameters, progress!, record.Rehearsal, CancellationToken.None);
                }
                catch (Exception ex) { _logger.LogError(ex, "Carrying on run {RunId} from its trying node failed", record.RunId); }
            });
        }
        return carriedOn;
    }

    private async Task<(PipelineDag? Dag, string? Refusal)> LoadAsync(Guid pipelineId, CancellationToken cancellationToken, Start? start = null)
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

        var reasons = DagValidator.Validate(dag).ToList();
        if (start is not null)
        {
            reasons.AddRange(DagValidator.UnfilledByParameters(dag, start.Parameters));
            if (!dag.Published && !start.Rehearsal)
                reasons.Add($"'{dag.Name}' is a draft, so it runs only as a rehearsal.");
        }
        return reasons.Count == 0 ? (dag, null) : (null, "Pipeline is invalid: " + string.Join(" ", reasons));
    }

    // What a sweep has settled so far: a fresh run starts with nothing, a resumed one with its record.
    private sealed class Progress
    {
        public Dictionary<Guid, NodeRunResult> Results { get; } = new();
        // Keyed by the wire itself, since two wires between the same ports are equal as values.
        public Dictionary<DagWire, WireDelivery> Deliveries { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Guid, NodeRunResult> Waiting { get; } = new();
        // Waiting nodes that already hold a vigil from an earlier sweep.
        public HashSet<Guid> Holding { get; } = new();
        // Nodes read back mid-trying, with the attempts their record holds, so the next attempt is numbered after them.
        public Dictionary<Guid, IReadOnlyList<AttemptRecord>> Trying { get; } = new();

        // Answers whether anything carried on from the node, so an answer that reaches no wire can end the run.
        public bool Settle(PipelineDag dag, NodeRunResult result)
        {
            Results[result.NodeId] = result;
            var carried = false;
            foreach (var (wire, delivery) in WireDeliveries.From(dag, dag.Node(result.NodeId)!, result))
            {
                Deliveries[wire] = delivery;
                carried = true;
            }
            return carried;
        }
    }

    // A node with no record is one the run had not reached, which only a node behind a wait can be; any other
    // missing record means the run would have to call that node again to carry on.
    private static (Progress Progress, DagNode? Missing) ProgressFrom(PipelineDag dag, RunRecord record)
    {
        var progress = new Progress();
        foreach (var node in dag.Nodes)
        {
            if (!record.Nodes.TryGetValue(node.NodeId, out var recorded) || recorded.Status == RunStatus.Running) continue;
            var result = new NodeRunResult(node.NodeId, node.Name, recorded.Status, recorded.Outputs, recorded.Error,
                Items: recorded.Items?.Select(item => new NodeRunResult(node.NodeId, node.Name, item.Status, item.Outputs, item.Error, Decision: item.Decision)).ToList(),
                Decision: recorded.Decision);
            if (recorded.Status == RunStatus.Waiting)
            {
                progress.Waiting[node.NodeId] = result;
                progress.Holding.Add(node.NodeId);
                continue;
            }
            if (recorded.Status == RunStatus.Trying)
            {
                progress.Trying[node.NodeId] = recorded.AttemptsMade;
                continue;
            }
            progress.Settle(dag, result);
        }

        var behindAPause = Downstream(dag, progress.Waiting.Keys.Concat(progress.Trying.Keys));
        var missing = dag.Nodes.FirstOrDefault(node => !progress.Results.ContainsKey(node.NodeId)
            && !progress.Waiting.ContainsKey(node.NodeId) && !progress.Trying.ContainsKey(node.NodeId) && !behindAPause.Contains(node.NodeId));
        return (progress, missing);
    }

    private static HashSet<Guid> Downstream(PipelineDag dag, IEnumerable<Guid> from)
    {
        var reached = new HashSet<Guid>();
        var frontier = new Stack<Guid>(from);
        while (frontier.Count > 0)
        {
            var node = frontier.Pop();
            foreach (var wire in dag.WiresOutOf(node))
                if (reached.Add(wire.To.Node.NodeId))
                    frontier.Push(wire.To.Node.NodeId);
        }
        return reached;
    }

    private async Task<PipelineRunResult> SweepAsync(
        Guid rid, PipelineDag dag, JsonElement runParams, Progress progress, bool rehearsal, CancellationToken cancellationToken)
    {
        try
        {
            return await SweepUntilSettledAsync(rid, dag, runParams, progress, rehearsal, cancellationToken);
        }
        finally
        {
            _stops.TryRemove(rid, out _);
        }
    }

    private async Task<PipelineRunResult> SweepUntilSettledAsync(
        Guid rid, PipelineDag dag, JsonElement runParams, Progress progress, bool rehearsal, CancellationToken cancellationToken)
    {
        var pipelineId = dag.PipelineId;
        var results = progress.Results;
        var deliveries = progress.Deliveries;
        var halted = false;
        string? unhandled = null;
        StopRequested? stopped = null;

        bool Unsettled(DagNode node) => !results.ContainsKey(node.NodeId) && !progress.Waiting.ContainsKey(node.NodeId);

        using var gate = new SemaphoreSlim(_maxConcurrency);
        var pending = dag.Nodes.Where(Unsettled).ToList();
        while (pending.Count > 0 && !halted && stopped is null)
        {
            // The Output node calls nothing, so a run left with only it to reach has finished its work.
            if (StopOf(rid) is { } stop && pending.Any(node => node.Kind != DagNodeKind.Output))
            {
                stopped = stop;
                break;
            }

            // A node behind a waiting one is not ready until the wait is over.
            var ready = pending.Where(n => dag.WiresInto(n.NodeId).All(w => results.ContainsKey(w.From.Node.NodeId))).ToList();
            if (ready.Count == 0) break;

            // A node no wire carried into is on a path not taken: it is recorded and nothing is called for it, so a
            // node where paths meet runs once, from the paths that carried.
            var notTaken = ready.Where(n => dag.WiresInto(n.NodeId).Any() && !dag.WiresInto(n.NodeId).Any(deliveries.ContainsKey)).ToList();
            foreach (var node in notTaken)
            {
                var result = new NodeRunResult(node.NodeId, node.Name, RunStatus.NotTaken, NoOutputs, null);
                progress.Settle(dag, result);
                await PersistStatusAsync(rid, result, cancellationToken);
            }

            var batch = await Task.WhenAll(ready.Except(notTaken).Select(node => RunNodeGuardedAsync(gate, dag, node, rid, deliveries, runParams,
                progress.Trying.TryGetValue(node.NodeId, out var made) ? made : [], rehearsal, cancellationToken)));

            foreach (var result in batch)
            {
                if (result.Status == RunStatus.Waiting)
                    progress.Waiting[result.NodeId] = result;
                else if (!progress.Settle(dag, result) && AnswerReachingNoWire(dag, result) is { } reason)
                {
                    halted = true;
                    unhandled = reason;
                }
                if (result.Status == RunStatus.CannotRehearse)
                    halted = true;
                if (result.Status == RunStatus.Cancelled)
                    stopped ??= StopOf(rid);
                await PersistStatusAsync(rid, result, cancellationToken);
            }
            pending = pending.Where(Unsettled).ToList();
        }
        if (!halted && stopped is null && progress.Waiting.Count > 0)
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
            unhandled = failures[0].Error;
        }

        // Anything still pending, or waiting, was blocked by an upstream failure, or not reached because the run was stopped.
        var pendingStatus = stopped is not null ? RunStatus.Cancelled : RunStatus.Skipped;
        foreach (var node in pending.Concat(progress.Waiting.Keys.Select(id => dag.Node(id)!)).Where(node => !results.ContainsKey(node.NodeId)))
        {
            var result = new NodeRunResult(node.NodeId, node.Name, pendingStatus, NoOutputs, stopped?.Reason);
            results[node.NodeId] = result;
            await PersistStatusAsync(rid, result, cancellationToken);
        }

        var outputNode = dag.Nodes.Single(n => n.Kind == DagNodeKind.Output);
        var failuresReachingTheEnd = dag.WiresInto(outputNode.NodeId)
            .Where(wire => deliveries.TryGetValue(wire, out var delivery) && delivery.CarriesAFail)
            .Select(wire => new DeliveredFailure(wire.From.Node.Name, wire.To.Port.PortName, results[wire.From.Node.NodeId].Error))
            .ToList();
        var verdict = OutputVerdict.Judge(
            outputNode, () => AssembleInputs(dag, outputNode, deliveries, runParams), failuresReachingTheEnd, _clock);
        var runStatus = stopped is not null ? RunStatus.Cancelled
            : unhandled is not null ? RunStatus.Failed
            : results.Values.Any(r => r.Status == RunStatus.CannotRehearse) ? RunStatus.CannotRehearse
            : !verdict.Passes ? RunStatus.Failed
            : rehearsal ? RunStatus.Rehearsed
            : RunStatus.Succeeded;
        await BestEffort(() => _gateway.SetRunStatusAsync(rid, runStatus, cancellationToken, stopped?.Reason), "set run status");
        if (stopped is not null) await BestEffort(() => _gateway.RelateStoppedByAsync(rid, stopped.RequestId, cancellationToken), "relate the stop");

        var success = runStatus is RunStatus.Succeeded or RunStatus.Rehearsed;
        var ordered = dag.Nodes.Select(n => results[n.NodeId]).ToList();

        // The pipeline's published result: the Output boundary node's collected inputs. Persisted on the
        // PipelineRun Thing (so it streams over SSE and is temporally queryable) and returned to the caller.
        JsonElement? runResult = null;
        if (success && results[outputNode.NodeId].Status is RunStatus.Succeeded or RunStatus.Rehearsed)
        {
            try
            {
                var collected = AssembleInputs(dag, outputNode, deliveries, runParams);
                runResult = JsonSerializer.SerializeToElement(collected);
                await BestEffort(() => _gateway.SetRunResultAsync(rid, runResult.Value, cancellationToken), "set run result");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to assemble the Output node result for run {RunId}", rid);
            }
        }

        return new PipelineRunResult(rid, pipelineId, success, ordered,
            success ? null
                : stopped is not null ? $"Run stopped: {stopped.Reason}"
                : runStatus == RunStatus.CannotRehearse ? "The rehearsal stopped at a node it cannot rehearse."
                : unhandled ?? verdict.Reason,
            runResult);
    }

    // An answer no wire carries on from would end the run short of its Output node, so the run fails and says where.
    private static string? AnswerReachingNoWire(PipelineDag dag, NodeRunResult result)
    {
        if (dag.Node(result.NodeId) is { Kind: DagNodeKind.Output })
            return result.Status == RunStatus.Failed ? result.Error : null;
        return result.Status switch
        {
            RunStatus.Failed => $"'{result.Name}' failed, and no fail wire leaves it.",
            RunStatus.Succeeded or RunStatus.Partial => $"'{result.Name}' succeeded, and no success wire leaves it.",
            _ => null,
        };
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
            var waiting = Stopwatch.StartNew();
            await _placing.WaitAsync(cancellationToken);
            var waited = waiting.ElapsedMilliseconds;
            var holding = Stopwatch.StartNew();
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
                _logger.LogInformation("Placing a vigil for run {RunId} waited {WaitedMilliseconds} ms for the placing lock and held it {HeldMilliseconds} ms",
                    runId, waited, holding.ElapsedMilliseconds);
            }
        }
        catch (Exception ex)
        {
            return $"its vigil could not be placed: {ex.Message}";
        }
    }

    private Task PersistStatusAsync(Guid runId, NodeRunResult result, CancellationToken cancellationToken) =>
        BestEffort(() => _gateway.SetNodeRunStatusAsync(runId, result.NodeId, result.Name, result.Status, result.Error, cancellationToken, outputs: result.Outputs,
            decision: result.Decision), "persist node run");

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

    private async Task<NodeRunResult> RunNodeGuardedAsync(
        SemaphoreSlim gate, PipelineDag dag, DagNode node, Guid runId,
        IReadOnlyDictionary<DagWire, WireDelivery> deliveries, JsonElement runParams,
        IReadOnlyList<AttemptRecord> attemptsMade, bool rehearsal, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await RunNodeAsync(dag, node, runId, deliveries, runParams, attemptsMade, rehearsal, cancellationToken);
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
        IReadOnlyDictionary<DagWire, WireDelivery> deliveries, JsonElement runParams, CancellationToken cancellationToken)
    {
        try
        {
            var sent = AssembleInputs(dag, node, deliveries, runParams);
            await _gateway.SendMessageAsync(runId, telling, JsonSerializer.SerializeToElement(sent), cancellationToken);
            return new NodeRunResult(node.NodeId, node.Name, RunStatus.Succeeded, sent, null);
        }
        catch (Exception ex)
        {
            return new NodeRunResult(node.NodeId, node.Name, RunStatus.Failed, NoOutputs,
                $"The message to '{telling.SystemName}' could not be written: {ex.Message}");
        }
    }

    // Each request is written on its own, so one the broker refuses leaves the others written, and the node names only
    // the Things it could not write one for.
    private async Task<NodeRunResult> StopThingsAsync(
        PipelineDag dag, DagNode node, Guid runId,
        IReadOnlyDictionary<DagWire, WireDelivery> deliveries, JsonElement runParams, CancellationToken cancellationToken)
    {
        Dictionary<string, JsonElement> inputs;
        try
        {
            inputs = AssembleInputs(dag, node, deliveries, runParams);
        }
        catch (Exception ex)
        {
            return Failure(node, $"What reaches '{node.Name}' could not be put together: {ex.Message}");
        }

        List<JsonElement> given = !inputs.TryGetValue(ModelNames.Things, out var things) ? []
            : things.ValueKind == JsonValueKind.Array ? things.EnumerateArray().ToList()
            : [things];
        var reason = inputs.TryGetValue(ModelNames.Reason, out var stated) && stated.ValueKind == JsonValueKind.String ? stated.GetString() : null;
        var written = 0;
        var unwritten = new List<string>();
        foreach (var value in given)
        {
            if (ThingNamedBy(value) is not { } thing)
            {
                unwritten.Add($"{value.GetRawText()}, which names no Thing");
                continue;
            }
            try
            {
                await _gateway.WriteStopRequestAsync(runId, thing.Id, reason, cancellationToken);
                written++;
            }
            catch (Exception ex)
            {
                unwritten.Add($"'{thing.Name}' ({ex.Message})");
            }
        }

        var outputs = new Dictionary<string, JsonElement> { [ModelNames.Requested] = JsonSerializer.SerializeToElement(written) };
        return unwritten.Count == 0
            ? new NodeRunResult(node.NodeId, node.Name, RunStatus.Succeeded, outputs, null)
            : new NodeRunResult(node.NodeId, node.Name, RunStatus.Failed, outputs,
                $"'{node.Name}' could not write a stop request for {string.Join("; ", unwritten)}.");
    }

    // What would have been sent, started or stopped is kept as the node's outputs, so the page can show it; nothing is
    // written but the record.
    private NodeRunResult Rehearse(
        PipelineDag dag, DagNode node, IReadOnlyDictionary<DagWire, WireDelivery> deliveries, JsonElement runParams)
    {
        try
        {
            return new NodeRunResult(node.NodeId, node.Name, RunStatus.Rehearsed, AssembleInputs(dag, node, deliveries, runParams), null);
        }
        catch (Exception ex)
        {
            return Failure(node, $"What reaches '{node.Name}' could not be put together: {ex.Message}");
        }
    }

    // The next run is recorded and swept on its own, as a run a state entry starts is, so this run carries on without
    // waiting for it. The subject carries over; what reached the node is the new run's parameters.
    private NodeRunResult StartFollowOn(
        PipelineDag dag, DagNode node, Starting starting, Guid runId,
        IReadOnlyDictionary<DagWire, WireDelivery> deliveries, JsonElement runParams)
    {
        Dictionary<string, JsonElement> reached;
        try
        {
            reached = AssembleInputs(dag, node, deliveries, runParams);
        }
        catch (Exception ex)
        {
            return Failure(node, $"'{starting.PipelineName}' could not be started from '{node.Name}': {ex.Message}");
        }

        var parameters = JsonSerializer.SerializeToElement(reached);
        var subject = SubjectCarriedBy(runParams);
        var nextRunId = Guid.NewGuid();
        _ = Task.Run(async () =>
        {
            try { await RunAsync(starting.PipelineId, parameters, CancellationToken.None, nextRunId, subject, startedBy: runId); }
            catch (Exception ex) { _logger.LogError(ex, "Run {NextRunId} of '{Pipeline}', started from run {RunId}, failed to run", nextRunId, starting.PipelineName, runId); }
        });
        reached[ModelNames.StartedRun] = JsonSerializer.SerializeToElement(nextRunId.ToString());
        return new NodeRunResult(node.NodeId, node.Name, RunStatus.Succeeded, reached, null);
    }

    private static RunSubject? SubjectCarriedBy(JsonElement runParams) =>
        runParams.ValueKind == JsonValueKind.Object && runParams.TryGetProperty(SubjectParam, out var carried)
            && ThingNamedBy(carried) is { } named
            ? new RunSubject(named.Id, named.Name)
            : null;

    private async Task<NodeRunResult> RunNodeAsync(
        PipelineDag dag, DagNode node, Guid runId,
        IReadOnlyDictionary<DagWire, WireDelivery> deliveries, JsonElement runParams,
        IReadOnlyList<AttemptRecord> attemptsMade, bool rehearsal, CancellationToken cancellationToken)
    {
        // Mark the node (aggregate) running before its service is called so the editor animates it live; the terminal status
        // is written by RunAsync from the value returned here.
        await BestEffort(() => _gateway.SetNodeRunStatusAsync(runId, node.NodeId, node.Name, RunStatus.Running, null, cancellationToken), "persist node running");

        // Boundary nodes never call a service: an Input node projects the run's params onto its output ports
        // (which then flow downstream via wires); an Output node is a sink — its inputs are collected as the
        // run result after the sweep (see RunAsync), so here it simply succeeds.
        if (node.Kind == DagNodeKind.Input)
            return new NodeRunResult(node.NodeId, node.Name, RunStatus.Succeeded, ProjectParamsOntoOutputs(node, runParams), null);
        if (node.Kind == DagNodeKind.Output)
            return new NodeRunResult(node.NodeId, node.Name, RunStatus.Succeeded, NoOutputs, null);
        if (node.Kind is DagNodeKind.Tell or DagNodeKind.FollowOn or DagNodeKind.Stop)
            return node switch
            {
                _ when rehearsal => Rehearse(dag, node, deliveries, runParams),
                { Kind: DagNodeKind.Stop } => await StopThingsAsync(dag, node, runId, deliveries, runParams, cancellationToken),
                { Tells: { } telling } => await TellAsync(dag, node, telling, runId, deliveries, runParams, cancellationToken),
                _ => StartFollowOn(dag, node, node.Starts!, runId, deliveries, runParams),
            };

        Dictionary<string, JsonElement> inputs;
        try
        {
            inputs = AssembleInputs(dag, node, deliveries, runParams);
        }
        catch (Exception ex)
        {
            return new NodeRunResult(node.NodeId, node.Name, RunStatus.Failed, NoOutputs, $"Wire transform failed: {ex.Message}");
        }

        if (rehearsal && node.Waits is { } awaitedState)
            return new NodeRunResult(node.NodeId, node.Name, RunStatus.CannotRehearse, NoOutputs,
                $"'{node.Name}' waits for '{awaitedState.RangeName}', which a rehearsal cannot do, so the rehearsal stops here.");
        if (rehearsal && !node.Rehearses)
            return new NodeRunResult(node.NodeId, node.Name, RunStatus.CannotRehearse, NoOutputs,
                $"'{node.Name}' calls '{node.Subdomain}', whose service does not rehearse, so the rehearsal stops here.");

        // Fan-out: if the node has a collection input and it carries a list, run the node once per item.
        var collection = node.CollectionInput;
        if (collection != null && inputs.TryGetValue(collection.PortName, out var collValue) && collValue.ValueKind == JsonValueKind.Array)
            return await RunFanOutAsync(node, runId, inputs, collection.PortName, collValue, rehearsal, cancellationToken);

        AwaitedThing? awaited = null;
        if (node.Waits is { } wait)
        {
            awaited = inputs.TryGetValue(wait.PortName, out var reaching) ? ThingNamedBy(reaching) : null;
            if (awaited is null)
                return Failure(node, $"'{node.Name}' waits for the Thing reaching its port '{wait.PortName}', but what reached it names no Thing.");
        }

        var (result, _) = await AttemptAsync(node, runId, inputs, index: null, total: 0, attemptsMade, rehearsal, cancellationToken);
        if (!rehearsal) await RecordProducedAsync(runId, node, index: null, result, cancellationToken);
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

    // Assemble a node's inputs: the Thing chosen on the node, then param-bound inputs, then wires. Each
    // wire extracts its from-path of the upstream output and deep-merges it at its to-path into the target
    // input, so several wires compose one input value; empty paths carry the whole payload and a scalar
    // wire overrides. Wires into a collection input are gathered into one list instead, in wire order.
    private Dictionary<string, JsonElement> AssembleInputs(
        PipelineDag dag, DagNode node,
        IReadOnlyDictionary<DagWire, WireDelivery> deliveries, JsonElement runParams)
    {
        var accumulated = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var (port, chosen) in node.Choices)
            accumulated[port] = new JsonObject { ["id"] = chosen.Id.ToString(), ["name"] = chosen.Name };
        foreach (var (port, paramKey) in node.ParameterBindings)
            if (TryGetParam(runParams, paramKey, out var bound))
                accumulated[port] = JsonSerializer.SerializeToNode(bound);
        foreach (var wire in dag.WiresInto(node.NodeId))
        {
            if (!deliveries.TryGetValue(wire, out var delivery)) continue;
            var extracted = PayloadMapping.Extract(delivery.Value, wire.FromPath);
            if (extracted is null) continue; // the from-path is not present in the upstream output
            var reshaped = string.IsNullOrEmpty(wire.Transform)
                ? extracted.Value
                : new JsonataTransform(wire.Transform).Eval(extracted.Value, _clock);
            var inPort = wire.To.Port.PortName;
            if (wire.To.Port.Collection)
            {
                var gathered = accumulated.TryGetValue(inPort, out var soFar) ? AsList(soFar) : new JsonArray();
                AppendItems(gathered, reshaped);
                accumulated[inPort] = gathered;
                continue;
            }
            var placed = PayloadMapping.Place(wire.ToPath, reshaped);
            accumulated[inPort] = accumulated.TryGetValue(inPort, out var existing)
                ? PayloadMapping.Merge(existing, placed)
                : placed;
        }

        var inputs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (port, node2) in accumulated)
            inputs[port] = PayloadMapping.ToElement(node2);
        return inputs;
    }

    // The subject is the broker's word on what entered, so it wins over a relationship property of the
    // same name, and it comes first.
    private static JsonElement WithSubject(JsonElement runParams, RunSubject subject)
    {
        var merged = new JsonObject { [SubjectParam] = new JsonObject { ["id"] = subject.Id.ToString(), ["name"] = subject.Name } };
        if (runParams.ValueKind == JsonValueKind.Object)
            foreach (var given in runParams.EnumerateObject().Where(given => given.Name != SubjectParam))
                merged[given.Name] = JsonSerializer.SerializeToNode(given.Value);
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
        string collectionPort, JsonElement items, bool rehearsal, CancellationToken cancellationToken)
    {
        var itemList = items.EnumerateArray().ToList();
        var total = itemList.Count;
        using var itemGate = new SemaphoreSlim(_maxConcurrency);
        var results = await Task.WhenAll(itemList.Select((item, i) =>
            RunItemAsync(node, runId, baseInputs, collectionPort, item, i, total, itemGate, rehearsal, cancellationToken)));

        var failures = results.Count(r => r.Status != RunStatus.Succeeded);
        var continueOnError = string.Equals(node.OnItemError, ModelNames.OnItemErrorContinue, StringComparison.OrdinalIgnoreCase);

        if (failures > 0 && !continueOnError)
        {
            var firstError = results.First(r => r.Status != RunStatus.Succeeded).Error ?? "an item failed";
            return new NodeRunResult(node.NodeId, node.Name, RunStatus.Failed, NoOutputs, $"Fan-out failed: {firstError}", Items: results);
        }

        var gathered = GatherOutputs(node, results);
        var status = failures == 0 ? RunStatus.Succeeded
            : failures == total ? RunStatus.Failed
            : RunStatus.Partial;
        return new NodeRunResult(node.NodeId, node.Name, status, gathered, failures > 0 ? $"{failures}/{total} items failed" : null, Items: results);
    }

    private async Task<NodeRunResult> RunItemAsync(
        DagNode node, Guid runId, IReadOnlyDictionary<string, JsonElement> baseInputs,
        string collectionPort, JsonElement item, int index, int total, SemaphoreSlim itemGate, bool rehearsal, CancellationToken cancellationToken)
    {
        await itemGate.WaitAsync(cancellationToken);
        try
        {
            await BestEffort(() => _gateway.SetNodeRunStatusAsync(runId, node.NodeId, node.Name, RunStatus.Running, null, cancellationToken, index, total), "persist item running");
            var inputs = new Dictionary<string, JsonElement>(baseInputs, StringComparer.Ordinal) { [collectionPort] = item };
            var (result, _) = await AttemptAsync(node, runId, inputs, index, total, [], rehearsal, cancellationToken);
            await BestEffort(() => _gateway.SetNodeRunStatusAsync(runId, node.NodeId, node.Name, result.Status, result.Error, cancellationToken, index, total, result.Outputs,
                result.Decision), "persist item terminal");
            if (!rehearsal) await RecordProducedAsync(runId, node, index, result, cancellationToken);
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
            if (node.OutPort(port) is { Collection: true })
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

    // A node with a retry policy is called again while its calls never reach the service, each attempt written as it
    // happens, with a pause between them measured on the model clock. Any answer from the service is final, a refusal
    // among them. GaveUp says the attempts or the trying time ran out, which is what a hand-over acts on.
    private async Task<(NodeRunResult Result, bool GaveUp)> AttemptAsync(
        DagNode node, Guid runId, IReadOnlyDictionary<string, JsonElement> inputs, int? index, int total,
        IReadOnlyList<AttemptRecord> attemptsMade, bool rehearsal, CancellationToken cancellationToken)
    {
        if (node.Retries is not { } policy)
            return ((await CallOnceAsync(node, runId, inputs, index, rehearsal, cancellationToken)).Result, false);

        var firstStarted = attemptsMade.Count > 0 ? attemptsMade[0].StartedUtc : _clock.GetUtcNow();
        var number = attemptsMade.Count;
        if (attemptsMade.Count > 0)
            await PauseUntilAsync(runId, attemptsMade[^1].FinishedUtc + policy.PauseBefore(number + 1, 0), cancellationToken);

        while (true)
        {
            if (StopOf(runId) is { } stop)
                return (new NodeRunResult(node.NodeId, node.Name, RunStatus.Cancelled, NoOutputs, stop.Reason), false);
            number++;
            var started = _clock.GetUtcNow();
            var outcome = await CallOnceAsync(node, runId, inputs, index, rehearsal, cancellationToken);
            var attempt = new AttemptRecord(number, started, _clock.GetUtcNow(), outcome.Result.Status, outcome.Result.Error);
            await BestEffort(() => _gateway.RecordAttemptAsync(runId, node.NodeId, index, attempt, cancellationToken), "record attempt");
            if (outcome.ReachedTheService) return (outcome.Result, false);
            if (number >= policy.Attempts) return (outcome.Result, true);

            var pause = policy.PauseBefore(number + 1, Random.Shared.NextDouble());
            if (attempt.FinishedUtc + pause > firstStarted + policy.TryingTime) return (outcome.Result, true);
            await BestEffort(() => _gateway.SetNodeRunStatusAsync(runId, node.NodeId, node.Name, RunStatus.Trying, null, cancellationToken, index, total), "persist node trying");
            await PauseUntilAsync(runId, attempt.FinishedUtc + pause, cancellationToken);
        }
    }

    // The pause is over when the model clock says so, not when the timer fires: a timer can fire a little early, and the
    // clock's rate can change while the pause runs. A clock that stands still can never reach the end, so it takes the
    // pause once in real time.
    private async Task PauseUntilAsync(Guid runId, DateTimeOffset modelEnd, CancellationToken cancellationToken)
    {
        var stop = _stops.GetOrAdd(runId, _ => NewStop()).Task;
        for (var remaining = modelEnd - _clock.GetUtcNow(); remaining > TimeSpan.Zero; remaining = modelEnd - _clock.GetUtcNow())
        {
            var ended = await Task.WhenAny(Task.Delay(remaining / (_clock.Rate > 0 ? _clock.Rate : 1.0), cancellationToken), stop);
            if (ended == stop || ended.IsCanceled || _clock.Rate <= 0) return;
        }
    }

    private sealed record CallOutcome(NodeRunResult Result, bool ReachedTheService);

    // A call that threw, or that the broker's door answered for a service it could not reach, never reached the
    // service; any other answer came from it.
    private async Task<CallOutcome> CallOnceAsync(
        DagNode node, Guid runId, IReadOnlyDictionary<string, JsonElement> inputs, int? index, bool rehearsal, CancellationToken cancellationToken)
    {
        var envelope = BuildEnvelope(runId, node, inputs, index, rehearsal);

        NodeCallResult nodeCall;
        try
        {
            nodeCall = await _gateway.CallAsync(node.Subdomain, envelope, cancellationToken);
        }
        catch (Exception ex)
        {
            return new CallOutcome(Failure(node, $"Service call to '{node.Subdomain}' failed: {ex.Message}"), ReachedTheService: false);
        }

        if (nodeCall.StatusCode is < 200 or >= 300)
            return new CallOutcome(Failure(node, $"Node '{node.Name}' returned HTTP {nodeCall.StatusCode}."), ReachedTheService: nodeCall.StatusCode < 500);

        return new CallOutcome(NodeChoices.Choose(node, inputs, ParseNodeResponse(node, nodeCall.Body), _clock), ReachedTheService: true);
    }

    private static readonly JsonElement NullJson = JsonSerializer.SerializeToElement<object?>(null);

    private static NodeRunResult ParseNodeResponse(DagNode node, string body)
    {
        try
        {
            var root = JsonDocument.Parse(body).RootElement;
            var answered = root.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
            if (answered is not (ModelNames.ResultSuccess or ModelNames.ResultFail))
                return Failure(node, $"'{node.Name}' answered no result: an answer says \"result\": \"{ModelNames.ResultSuccess}\" or \"{ModelNames.ResultFail}\".");

            var outputs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (root.TryGetProperty("outputs", out var o) && o.ValueKind == JsonValueKind.Object)
                foreach (var p in o.EnumerateObject())
                    outputs[p.Name] = p.Value.Clone();

            if (answered == ModelNames.ResultFail)
            {
                var error = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
                return new NodeRunResult(node.NodeId, node.Name, RunStatus.Failed, outputs, error ?? $"'{node.Name}' answered fail.");
            }

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

    // index is the fan-out item's, null for a single call; rehearsal is sent only when true, so a block that knows
    // nothing of rehearsals reads the envelope it always has.
    private static JsonElement BuildEnvelope(Guid runId, DagNode node, IReadOnlyDictionary<string, JsonElement> inputs, int? index, bool rehearsal)
    {
        var envelope = new JsonObject
        {
            ["runId"] = runId,
            ["nodeId"] = node.NodeId,
            ["index"] = index,
            ["params"] = JsonSerializer.SerializeToNode(node.Params),
            ["inputs"] = JsonSerializer.SerializeToNode(inputs),
        };
        if (rehearsal) envelope[ModelNames.Rehearsal] = true;
        return JsonSerializer.SerializeToElement(envelope);
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
