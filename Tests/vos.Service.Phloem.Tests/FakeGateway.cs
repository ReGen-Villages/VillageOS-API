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

    public Func<string, JsonElement, NodeDispatchResult> OnDispatch { get; set; } = (_, _) => new NodeDispatchResult(200, "{\"success\":true,\"outputs\":{}}");
    public Func<Guid, bool> CancelRequested { get; set; } = _ => false;
    public List<string> Dispatched { get; } = new();
    public List<(string Subdomain, JsonElement Envelope)> Envelopes { get; } = new();
    public List<string> StatusUpdates { get; } = new();
    public List<(string Name, string Status)> NodeStatuses { get; } = new();
    public List<(Guid RunId, Guid PipelineId, RunSubject? Subject, Guid? RequestId, Guid? StartedBy)> RunsCreated { get; } = new();
    public List<Guid> StartTargetsAsked { get; } = new();
    public JsonElement? RunResult { get; private set; }

    // A graph caches what it has resolved, as each snapshot the broker answers does, so runs read at once each need
    // their own; set this where runs start concurrently.
    public Func<PipelineGraph>? FreshGraph { get; set; }

    public Task<PipelineGraph> LoadPipelineSubgraphAsync(Guid pipelineId, CancellationToken ct) =>
        LoadFailure is { } failure ? Task.FromException<PipelineGraph>(failure) : Task.FromResult(FreshGraph?.Invoke() ?? Graph);

    public Task<PipelineGraph> LoadStartSubgraphAsync(Guid targetId, CancellationToken ct)
    {
        lock (StartTargetsAsked) StartTargetsAsked.Add(targetId);
        return Task.FromResult(FreshGraph?.Invoke() ?? Graph);
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

    public Task CreateRunAsync(Guid runId, Guid pipelineId, CancellationToken ct, RunSubject? subject = null, Guid? requestId = null, JsonElement? parameters = null, Guid? startedBy = null)
    {
        lock (RunsCreated) RunsCreated.Add((runId, pipelineId, subject, requestId, startedBy));
        var recorded = parameters is { ValueKind: not JsonValueKind.Undefined } given ? given.Clone() : (JsonElement?)null;
        ParametersRecorded = recorded;
        lock (_model) Runs[runId] = (pipelineId, recorded ?? default);
        return Task.CompletedTask;
    }

    public List<(string Name, int? Index, string Status, IReadOnlyDictionary<string, JsonElement>? Outputs)> NodeRunsPersisted { get; } = new();
    public List<(string Name, string Status, string Error)> NodeErrorsWritten { get; } = new();

    // What the model holds, as the writes below leave it, so a run can be paused and resumed from its record.
    private readonly object _model = new();
    public Dictionary<Guid, (Guid PipelineId, JsonElement Parameters)> Runs { get; } = new();
    public Dictionary<(Guid RunId, Guid NodeId), NodeRecord> Records { get; } = new();
    public Dictionary<(Guid RunId, Guid NodeId), Guid> Awaited { get; } = new();
    public Dictionary<(Guid RunId, Guid NodeId), Guid> HeldVigils { get; } = new();
    public List<(Guid VigilId, Guid SubjectId, Guid RangeId)> VigilsPlaced { get; } = new();
    public HashSet<Guid> LiveVigils { get; } = new();
    public string? PlacementRefusal { get; set; }
    public Exception? PlacementFailure { get; set; }
    public Action<Guid> OnVigilPlaced { get; set; } = _ => { };
    public Exception? RecordReadFailure { get; set; }

    public Func<Guid, string, bool> RefuseNodeRunWrite { get; set; } = (_, _) => false;

    public Task SetNodeRunStatusAsync(Guid runId, Guid nodeId, string nodeName, string status, string? error, CancellationToken ct, int? index = null, int total = 0,
        IReadOnlyDictionary<string, JsonElement>? outputs = null)
    {
        lock (NodeStatuses)
        {
            NodeStatuses.Add((nodeName, status));
            NodeRunsPersisted.Add((nodeName, index, status, outputs));
            if (error is not null) NodeErrorsWritten.Add((nodeName, status, error));
        }
        if (RefuseNodeRunWrite(nodeId, status)) throw new HttpRequestException("the model refused the write");
        if (index is null)
            lock (_model)
            {
                var before = Records.TryGetValue((runId, nodeId), out var kept) ? kept : null;
                Records[(runId, nodeId)] = new NodeRecord(status, outputs ?? before?.Outputs ?? new Dictionary<string, JsonElement>(), before?.Attempts);
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
        }
        return Task.CompletedTask;
    }

    public List<TryingNodeRun> TryingItems { get; } = new();

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

    public Task<IReadOnlyList<TryingNodeRun>> TryingNodeRunsAsync(CancellationToken ct)
    {
        lock (_model)
            return Task.FromResult<IReadOnlyList<TryingNodeRun>>(Records.Where(record => record.Value.Status == RunStatus.Trying)
                .Select(record => new TryingNodeRun(record.Key.RunId, record.Key.NodeId, PerItem: false))
                .Concat(TryingItems).ToList());
    }

    public Task<RunRecord?> ReadRunRecordAsync(Guid runId, CancellationToken ct)
    {
        if (RecordReadFailure is { } failure) return Task.FromException<RunRecord?>(failure);
        lock (_model)
        {
            if (!Runs.TryGetValue(runId, out var run)) return Task.FromResult<RunRecord?>(null);
            var nodes = Records.Where(record => record.Key.RunId == runId).ToDictionary(record => record.Key.NodeId, record => record.Value);
            return Task.FromResult<RunRecord?>(new RunRecord(runId, run.PipelineId, run.Parameters, nodes));
        }
    }

    public Task RelateAwaitedAsync(Guid runId, Guid nodeId, Guid awaitedId, CancellationToken ct)
    {
        lock (_model) Awaited[(runId, nodeId)] = awaitedId;
        return Task.CompletedTask;
    }

    public Task ForgetAwaitedAsync(Guid runId, Guid nodeId, CancellationToken ct)
    {
        lock (_model) Awaited.Remove((runId, nodeId));
        return Task.CompletedTask;
    }

    public Task<VigilPlacement> PlaceVigilAsync(Guid subjectId, Guid rangeId, CancellationToken ct)
    {
        if (PlacementFailure is { } failure) return Task.FromException<VigilPlacement>(failure);
        if (PlacementRefusal is { } reason) return Task.FromResult<VigilPlacement>(new VigilPlacement.Refused(reason));
        var vigilId = Guid.NewGuid();
        lock (_model)
        {
            VigilsPlaced.Add((vigilId, subjectId, rangeId));
            LiveVigils.Add(vigilId);
        }
        OnVigilPlaced(vigilId);
        return Task.FromResult<VigilPlacement>(new VigilPlacement.Placed(vigilId));
    }

    public Task RelateHeldVigilAsync(Guid runId, Guid nodeId, Guid vigilId, CancellationToken ct)
    {
        lock (_model) HeldVigils[(runId, nodeId)] = vigilId;
        return Task.CompletedTask;
    }

    public Task<VigilHolder?> HolderOfVigilAsync(Guid vigilId, CancellationToken ct)
    {
        lock (_model)
        {
            var held = HeldVigils.Where(entry => entry.Value == vigilId).Select(entry => entry.Key).ToList();
            return Task.FromResult(held.Count == 0 ? null : new VigilHolder(held[0].RunId, held[0].NodeId));
        }
    }

    public Task<IReadOnlyList<HeldWait>> WaitingNodeRunsAsync(CancellationToken ct)
    {
        lock (_model)
            return Task.FromResult<IReadOnlyList<HeldWait>>(Records.Where(record => record.Value.Status == RunStatus.Waiting)
                .Select(record => new HeldWait(record.Key.RunId, record.Key.NodeId,
                    Awaited.TryGetValue(record.Key, out var awaited) ? awaited : null,
                    HeldVigils.TryGetValue(record.Key, out var vigil) ? vigil : null))
                .ToList());
    }

    public Task<bool> IsVigilLiveAsync(Guid vigilId, CancellationToken ct)
    {
        lock (_model) return Task.FromResult(LiveVigils.Contains(vigilId));
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
        return Task.CompletedTask;
    }
    public Task SetRunResultAsync(Guid runId, JsonElement result, CancellationToken ct) { RunResult = result.Clone(); return Task.CompletedTask; }
    public Task<bool> IsCancelRequestedAsync(Guid runId, CancellationToken ct) => Task.FromResult(CancelRequested(runId));

    public Task<NodeDispatchResult> DispatchAsync(string subdomain, JsonElement envelope, CancellationToken ct)
    {
        lock (Dispatched) { Dispatched.Add(subdomain); Envelopes.Add((subdomain, envelope.Clone())); }
        return Task.FromResult(OnDispatch(subdomain, envelope));
    }
}
