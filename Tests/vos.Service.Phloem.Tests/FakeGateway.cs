using System.Text.Json;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;

namespace vos.Service.Phloem.Tests;

// Mycelium faked behind the gateway seam: one graph answers every read, and every write is recorded so a
// test can say what the orchestrator told the model.
internal sealed class FakeGateway : IMyceliumGateway
{
    public FakeGateway(PipelineGraph graph) => Graph = graph;

    public PipelineGraph Graph { get; set; }
    public Exception? LoadFailure { get; set; }

    public Func<string, JsonElement, NodeCallResult> OnCall { get; set; } = (_, _) => new NodeCallResult(200, "{\"result\":\"success\",\"outputs\":{}}");
    public List<string> Called { get; } = new();
    public List<(string Subdomain, JsonElement Envelope)> Envelopes { get; } = new();
    public List<string> StatusUpdates { get; } = new();
    public List<(string Name, string Status)> NodeStatuses { get; } = new();
    public List<(Guid RunId, Guid PipelineId, RunSubject? Subject, Guid? RequestId, Guid? StartedBy, Guid? Repeats)> RunsCreated { get; } = new();
    public List<Guid> StartTargetsAsked { get; } = new();
    public List<Guid> RelatedFromAsked { get; } = new();
    public List<IReadOnlyCollection<Guid>> StartReadsWidenedBy { get; } = new();
    public JsonElement? RunResult { get; private set; }

    // A graph caches what it has resolved, as each snapshot the broker answers does, so runs read at once each need
    // their own; set this where runs start concurrently.
    public Func<PipelineGraph>? FreshGraph { get; set; }

    public Task<PipelineGraph> LoadPipelineSubgraphAsync(Guid pipelineId, CancellationToken ct) =>
        LoadFailure is { } failure ? Task.FromException<PipelineGraph>(failure) : Task.FromResult(FreshGraph?.Invoke() ?? Graph);

    public Task<PipelineGraph> LoadStartSubgraphAsync(Guid targetId, IReadOnlyCollection<Guid> arrivingRelatesTo, CancellationToken ct)
    {
        lock (StartTargetsAsked) StartTargetsAsked.Add(targetId);
        lock (StartReadsWidenedBy) StartReadsWidenedBy.Add(arrivingRelatesTo);
        return Task.FromResult(FreshGraph?.Invoke() ?? Graph);
    }

    public Task<IReadOnlyList<Guid>> RelatedFromAsync(Guid thingId, CancellationToken ct)
    {
        lock (RelatedFromAsked) RelatedFromAsked.Add(thingId);
        IReadOnlyList<Guid> related = Graph.Thing(thingId) is { } thing ? Graph.TargetIdsFrom(thing).ToList() : [];
        return Task.FromResult(related);
    }

    public List<Guid> SubjectsRead { get; } = new();

    public Task<RunSubject?> SubjectAsync(Guid thingId, CancellationToken ct)
    {
        lock (SubjectsRead) SubjectsRead.Add(thingId);
        return Task.FromResult(Graph.Thing(thingId) is { } thing ? new RunSubject(thing.Id, thing.Name) : null);
    }

    // The relationships the model holds, by id, answered with their predicate; any other id is one it does not hold.
    public Dictionary<Guid, Guid> PredicatesByRelationship { get; } = new();
    public List<Guid> RelationshipsRead { get; } = new();

    public Task<Guid?> PredicateOfAsync(Guid relationshipId, CancellationToken ct)
    {
        RelationshipsRead.Add(relationshipId);
        return Task.FromResult(PredicatesByRelationship.TryGetValue(relationshipId, out var predicate) ? predicate : (Guid?)null);
    }

    public Action OnSend { get; set; } = () => { };
    public List<(Guid RunId, Telling Telling, JsonElement Payload)> MessagesSent { get; } = new();

    public Task SendMessageAsync(Guid runId, Telling telling, JsonElement payload, CancellationToken ct)
    {
        OnSend();
        MessagesSent.Add((runId, telling, payload.Clone()));
        return Task.CompletedTask;
    }

    public JsonElement? ParametersRecorded { get; private set; }

    public HashSet<Guid> Rehearsals { get; } = new();

    public Exception? RunCreationFailure { get; set; }

    public Task RunCreationHeldUntil { get; set; } = Task.CompletedTask;

    public bool RunCreationStarted { get; private set; }

    public async Task CreateRunAsync(Guid runId, Guid pipelineId, CancellationToken ct, RunSubject? subject = null, Guid? requestId = null, JsonElement? parameters = null, Guid? startedBy = null,
        string? refusal = null, bool rehearsal = false, Guid? repeats = null)
    {
        RunCreationStarted = true;
        await RunCreationHeldUntil;
        if (RunCreationFailure is not null) throw RunCreationFailure;
        if (rehearsal) lock (Rehearsals) Rehearsals.Add(runId);
        lock (RunsCreated) RunsCreated.Add((runId, pipelineId, subject, requestId, startedBy, repeats));
        if (refusal is not null)
        {
            StatusUpdates.Add(RunStatus.Refused);
            RunError = refusal;
        }
        var recorded = parameters is { ValueKind: not JsonValueKind.Undefined } given ? given.Clone() : (JsonElement?)null;
        ParametersRecorded = recorded;
        lock (_model)
        {
            Runs[runId] = (pipelineId, recorded ?? default);
            RunStatuses[runId] = refusal is null ? RunStatus.Running : RunStatus.Refused;
        }
    }

    public List<(string Name, int? Index, string Status, IReadOnlyDictionary<string, JsonElement>? Outputs)> NodeRunsPersisted { get; } = new();
    public List<(string Name, string Status, string Error)> NodeErrorsWritten { get; } = new();

    // What the model holds, as the writes below leave it, so a run can be paused and resumed from its record.
    private readonly object _model = new();
    public Dictionary<Guid, (Guid PipelineId, JsonElement Parameters)> Runs { get; } = new();
    public Dictionary<(Guid RunId, Guid NodeId), NodeRecord> Records { get; } = new();
    private readonly Dictionary<(Guid RunId, Guid NodeId), SortedList<int, NodeRecord>> _itemRecords = new();
    public Exception? RecordReadFailure { get; set; }

    public Func<Guid, string, bool> RefuseNodeRunWrite { get; set; } = (_, _) => false;

    private readonly List<(string Name, int Index, int Total)> _itemNumbersWritten = new();

    public List<(int Index, int Total)> ItemRecordsOf(string nodeName)
    {
        lock (NodeStatuses)
            return _itemNumbersWritten.Where(item => item.Name == nodeName).Select(item => (item.Index, item.Total)).Distinct().OrderBy(item => item.Index).ToList();
    }

    public Task SetNodeRunStatusAsync(Guid runId, Guid nodeId, string nodeName, string status, string? error, CancellationToken ct, int? index = null, int total = 0,
        IReadOnlyDictionary<string, JsonElement>? outputs = null, Guid? decision = null, Watch? watch = null)
    {
        lock (NodeStatuses)
        {
            NodeStatuses.Add((nodeName, status));
            NodeRunsPersisted.Add((nodeName, index, status, outputs));
            if (index is int numbered) _itemNumbersWritten.Add((nodeName, numbered, total));
            if (error is not null) NodeErrorsWritten.Add((nodeName, status, error));
            if (watch is not null) WatchWrites.Add((nodeName, status, watch));
        }
        if (RefuseNodeRunWrite(nodeId, status)) throw new HttpRequestException("the model refused the write");
        if (status == RunStatus.Waiting && watch is not null && WatchRefusal is { } refusal) throw new InvalidOperationException(refusal);
        Guid nodeRunId;
        lock (_model)
        {
            nodeRunId = NodeRunIdOf(runId, nodeId);
            if (index is null && watch is not null)
            {
                if (status == RunStatus.Waiting)
                {
                    var held = Watches.GetValueOrDefault((runId, nodeId));
                    Watches[(runId, nodeId)] = new Watch(watch.AwaitedId ?? held?.AwaitedId, watch.RangeId ?? held?.RangeId);
                }
                else
                {
                    Watches.Remove((runId, nodeId));
                    Answers.Remove((runId, nodeId));
                }
            }
        }
        if (index is null && status == RunStatus.Waiting && watch is { IsWhole: true }) OnWatchPlaced(nodeRunId);
        lock (_model)
        {
            if (index is int item)
            {
                if (!_itemRecords.TryGetValue((runId, nodeId), out var items))
                    _itemRecords[(runId, nodeId)] = items = new SortedList<int, NodeRecord>();
                items[item] = new NodeRecord(status, outputs ?? new Dictionary<string, JsonElement>(), Error: error, Decision: decision,
                    Number: new ItemNumber(item, total));
            }
            else
            {
                var before = Records.TryGetValue((runId, nodeId), out var kept) ? kept : null;
                Records[(runId, nodeId)] = new NodeRecord(status, outputs ?? before?.Outputs ?? new Dictionary<string, JsonElement>(), before?.Attempts,
                    error ?? before?.Error, Decision: decision ?? before?.Decision);
            }
        }
        return Task.CompletedTask;
    }

    public List<(Guid RunId, Guid NodeId, int? Index, AttemptRecord Attempt)> AttemptsRecorded { get; } = new();

    public Task RecordAttemptAsync(Guid runId, Guid nodeId, int? index, AttemptRecord attempt, CancellationToken ct)
    {
        lock (_model)
        {
            AttemptsRecorded.Add((runId, nodeId, index, attempt));
            if (index is null && Records.TryGetValue((runId, nodeId), out var record))
                Records[(runId, nodeId)] = record with { Attempts = [.. record.AttemptsMade, attempt] };
            if (index is int item && _itemRecords.TryGetValue((runId, nodeId), out var items) && items.TryGetValue(item, out var itemRecord))
                items[item] = itemRecord with { Attempts = [.. itemRecord.AttemptsMade, attempt] };
        }
        return Task.CompletedTask;
    }

    public void RecordItem(Guid runId, Guid nodeId, NodeRecord item)
    {
        lock (_model)
        {
            if (!_itemRecords.TryGetValue((runId, nodeId), out var items))
                _itemRecords[(runId, nodeId)] = items = new SortedList<int, NodeRecord>();
            items[item.Number!.Index] = item;
        }
    }

    public List<Cadence> Cadences { get; } = new();
    public List<(Guid CadenceId, DateTimeOffset Due)> LastDueWritten { get; } = new();
    public Exception? LastDueFailure { get; set; }

    public Exception? CadencesFailure { get; set; }

    public Task<IReadOnlyList<Cadence>> CadencesAsync(CancellationToken ct)
    {
        if (CadencesFailure is { } failure) return Task.FromException<IReadOnlyList<Cadence>>(failure);
        lock (Cadences) return Task.FromResult<IReadOnlyList<Cadence>>(Cadences.ToList());
    }

    public Task SetLastDueAsync(Guid cadenceId, DateTimeOffset due, CancellationToken ct)
    {
        if (LastDueFailure is { } failure) return Task.FromException(failure);
        lock (Cadences)
        {
            LastDueWritten.Add((cadenceId, due));
            var index = Cadences.FindIndex(cadence => cadence.Id == cadenceId);
            if (index >= 0) Cadences[index] = Cadences[index] with { LastDue = due };
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UnfinishedNodeRun>> UnfinishedNodeRunsAsync(CancellationToken ct)
    {
        lock (_model)
            return Task.FromResult<IReadOnlyList<UnfinishedNodeRun>>(Records.Where(record => Unfinished(record.Value)).Select(record => record.Key)
                .Concat(_itemRecords.Where(items => items.Value.Values.Any(Unfinished)).Select(items => items.Key))
                .Select(key => new UnfinishedNodeRun(key.RunId, key.NodeId)).ToList());
    }

    private static bool Unfinished(NodeRecord record) => record.Status is RunStatus.Trying or RunStatus.Running;

    public Task<RunRecord?> ReadRunRecordAsync(Guid runId, CancellationToken ct)
    {
        if (RecordReadFailure is { } failure) return Task.FromException<RunRecord?>(failure);
        lock (_model)
        {
            if (!Runs.TryGetValue(runId, out var run)) return Task.FromResult<RunRecord?>(null);
            var nodes = Records.Where(record => record.Key.RunId == runId).ToDictionary(record => record.Key.NodeId,
                record => (_itemRecords.TryGetValue(record.Key, out var items) ? record.Value with { Items = items.Values.ToList() } : record.Value)
                    with { Watching = Watches.GetValueOrDefault(record.Key) });
            bool rehearsal;
            lock (Rehearsals) rehearsal = Rehearsals.Contains(runId);
            Guid? repeatedBy;
            lock (RunsCreated) repeatedBy = RunsCreated.Where(created => created.Repeats == runId).Select(created => (Guid?)created.RunId).FirstOrDefault();
            return Task.FromResult<RunRecord?>(new RunRecord(runId, run.PipelineId, run.Parameters, nodes, rehearsal, RunStatuses.GetValueOrDefault(runId, RunStatus.Running),
                repeatedBy));
        }
    }

    public Dictionary<(Guid RunId, Guid NodeId), Watch> Watches { get; } = new();
    public Dictionary<(Guid RunId, Guid NodeId), WaitAnswer> Answers { get; } = new();
    public List<(string Name, string Status, Watch Watch)> WatchWrites { get; } = new();
    public string? WatchRefusal { get; set; }

    // Called as a watch's second relationship commits, where the platform answers a Thing already in the range.
    public Action<Guid> OnWatchPlaced { get; set; } = _ => { };

    private readonly Dictionary<Guid, (Guid RunId, Guid NodeId)> _nodeRuns = new();

    public Guid NodeRunIdOf(Guid runId, Guid nodeId)
    {
        lock (_model)
        {
            var id = new Guid(System.Security.Cryptography.MD5.HashData([.. runId.ToByteArray(), .. nodeId.ToByteArray()]));
            _nodeRuns[id] = (runId, nodeId);
            return id;
        }
    }

    public List<(Guid RunId, Guid NodeId)> ResumesWritten { get; } = new();

    public Task ResumeFromWaitAsync(Guid runId, Guid nodeId, string nodeName, IReadOnlyDictionary<string, JsonElement> outputs, Guid? decision, Watch watch,
        CancellationToken ct)
    {
        lock (NodeStatuses)
        {
            NodeStatuses.Add((nodeName, RunStatus.Succeeded));
            NodeRunsPersisted.Add((nodeName, null, RunStatus.Succeeded, outputs));
            WatchWrites.Add((nodeName, RunStatus.Succeeded, watch));
        }
        StatusUpdates.Add(RunStatus.Running);
        lock (_model)
        {
            ResumesWritten.Add((runId, nodeId));
            var before = Records.TryGetValue((runId, nodeId), out var kept) ? kept : null;
            Records[(runId, nodeId)] = new NodeRecord(RunStatus.Succeeded, outputs, before?.Attempts, before?.Error, Decision: decision ?? before?.Decision);
            Watches.Remove((runId, nodeId));
            Answers.Remove((runId, nodeId));
            RunStatuses[runId] = RunStatus.Running;
        }
        return Task.CompletedTask;
    }

    public Task<HeldNodeRun?> NodeRunAsync(Guid thingId, CancellationToken ct)
    {
        lock (_model)
        {
            return Task.FromResult(_nodeRuns.TryGetValue(thingId, out var key) && Records.ContainsKey(key) ? new HeldNodeRun(key.RunId, key.NodeId) : null);
        }
    }

    public Task<IReadOnlyList<HeldWait>> WaitingNodeRunsAsync(CancellationToken ct)
    {
        lock (_model)
            return Task.FromResult<IReadOnlyList<HeldWait>>(Records.Where(record => record.Value.Status == RunStatus.Waiting)
                .Select(record => new HeldWait(record.Key.RunId, record.Key.NodeId,
                    Watches.GetValueOrDefault(record.Key) ?? new Watch(null, null), Answers.GetValueOrDefault(record.Key)))
                .ToList());
    }

    public List<(Guid NodeId, int? Index, IReadOnlyList<Guid> Things)> Produced { get; } = new();

    public Task RelateProducedAsync(Guid runId, Guid nodeId, int? index, IReadOnlyList<Guid> producedThingIds, CancellationToken ct)
    {
        lock (Produced) Produced.Add((nodeId, index, producedThingIds));
        return Task.CompletedTask;
    }

    public string? RunError { get; private set; }

    public Task SetRunStatusAsync(Guid runId, string status, CancellationToken ct, string? error = null)
    {
        StatusUpdates.Add(status);
        if (error != null) RunError = error;
        lock (_model) RunStatuses[runId] = status;
        return Task.CompletedTask;
    }

    public Dictionary<Guid, string> RunStatuses { get; } = new();

    public string? RunStatusOf(Guid runId)
    {
        lock (_model) return RunStatuses.GetValueOrDefault(runId);
    }

    // The predicate the model binds to runStopping; a relationship the broker sends along it is a stop.
    public Guid StopsConnection { get; } = Guid.NewGuid();
    public string StopReason { get; set; } = "withdrawn by its owner";
    public List<(Guid RunId, Guid StopRequestId)> StoppedBy { get; } = new();

    public Task<bool> IsStopAsync(Guid relationshipId, CancellationToken ct) =>
        Task.FromResult(PredicatesByRelationship.TryGetValue(relationshipId, out var predicate) && predicate == StopsConnection);

    public Task<StopRequested> StopRequestedAsync(Guid stopRequestId, Guid targetId, CancellationToken ct)
    {
        Guid? requestedBy;
        lock (StopRequestsWritten) requestedBy = StopRequestsWritten.FirstOrDefault(written => written.RequestId == stopRequestId)?.RunId;
        lock (_model)
        {
            if (Runs.ContainsKey(targetId))
                return Task.FromResult(new StopRequested(stopRequestId, StopReason, targetId == requestedBy ? [] : [targetId]));
            List<Guid> about;
            lock (RunsCreated) about = RunsCreated.Where(run => run.Subject?.Id == targetId).Select(run => run.RunId).ToList();
            var going = about.Where(run => run != requestedBy && RunStatuses.GetValueOrDefault(run) is RunStatus.Running or RunStatus.Waiting or RunStatus.Trying).ToList();
            return Task.FromResult(new StopRequested(stopRequestId, StopReason, going));
        }
    }

    public sealed record StopRequestWritten(Guid RequestId, Guid RunId, Guid ThingId, string? Reason);

    public List<StopRequestWritten> StopRequestsWritten { get; } = new();
    public HashSet<Guid> StopRequestWriteFailsFor { get; } = new();
    public Action<StopRequestWritten> OnStopRequestWritten { get; set; } = _ => { };

    public Task WriteStopRequestAsync(Guid runId, Guid thingId, string? reason, CancellationToken ct)
    {
        if (StopRequestWriteFailsFor.Contains(thingId)) throw new HttpRequestException("the broker refused the write");
        var written = new StopRequestWritten(Guid.NewGuid(), runId, thingId, reason);
        lock (StopRequestsWritten) StopRequestsWritten.Add(written);
        OnStopRequestWritten(written);
        return Task.CompletedTask;
    }

    public Task RelateStoppedByAsync(Guid runId, Guid stopRequestId, CancellationToken ct)
    {
        lock (StoppedBy) StoppedBy.Add((runId, stopRequestId));
        return Task.CompletedTask;
    }

    public Task SetRunResultAsync(Guid runId, JsonElement result, CancellationToken ct) { RunResult = result.Clone(); return Task.CompletedTask; }

    public Task<NodeCallResult> CallAsync(string subdomain, JsonElement envelope, CancellationToken ct)
    {
        lock (Called) { Called.Add(subdomain); Envelopes.Add((subdomain, envelope.Clone())); }
        return OnCallAsync?.Invoke(subdomain, envelope) ?? Task.FromResult(OnCall(subdomain, envelope));
    }

    // OnCall answers before the sweep can start anything else; this answers when the test completes the task.
    public Func<string, JsonElement, Task<NodeCallResult>>? OnCallAsync { get; set; }

    public List<string> CalledSoFar()
    {
        lock (Called) return Called.ToList();
    }
}
