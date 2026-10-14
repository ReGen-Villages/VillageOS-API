using System.Text.Json;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;

namespace vos.Service.Phloem.Tests;

// Mycelium faked behind the gateway seam: one graph answers every read, and every write is recorded so a
// test can say what the orchestrator told the model.
internal sealed class FakeGateway : IMyceliumGateway
{
    private readonly PipelineGraph _graph;
    public FakeGateway(PipelineGraph graph) => _graph = graph;

    public Func<string, JsonElement, NodeDispatchResult> OnDispatch { get; set; } = (_, _) => new NodeDispatchResult(200, "{\"success\":true,\"outputs\":{}}");
    public Func<Guid, bool> CancelRequested { get; set; } = _ => false;
    public List<string> Dispatched { get; } = new();
    public List<(string Subdomain, JsonElement Envelope)> Envelopes { get; } = new();
    public List<string> StatusUpdates { get; } = new();
    public List<(string Name, string Status)> NodeStatuses { get; } = new();
    public List<(Guid RunId, Guid PipelineId, RunSubject? Subject, Guid? RequestId)> RunsCreated { get; } = new();
    public List<Guid> StartTargetsAsked { get; } = new();
    public JsonElement? RunResult { get; private set; }

    public Task<PipelineGraph> LoadPipelineSubgraphAsync(Guid pipelineId, CancellationToken ct) => Task.FromResult(_graph);

    public Task<PipelineGraph> LoadStartSubgraphAsync(Guid targetId, CancellationToken ct)
    {
        StartTargetsAsked.Add(targetId);
        return Task.FromResult(_graph);
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

    public Task CreateRunAsync(Guid runId, Guid pipelineId, CancellationToken ct, RunSubject? subject = null, Guid? requestId = null, JsonElement? parameters = null)
    {
        RunsCreated.Add((runId, pipelineId, subject, requestId));
        ParametersRecorded = parameters is { ValueKind: not JsonValueKind.Undefined } given ? given.Clone() : null;
        return Task.CompletedTask;
    }

    public List<(string Name, int? Index, string Status, IReadOnlyDictionary<string, JsonElement>? Outputs)> NodeRunsPersisted { get; } = new();

    public Task SetNodeRunStatusAsync(Guid runId, Guid nodeId, string nodeName, string status, string? error, CancellationToken ct, int? index = null, int total = 0,
        IReadOnlyDictionary<string, JsonElement>? outputs = null)
    {
        lock (NodeStatuses) { NodeStatuses.Add((nodeName, status)); NodeRunsPersisted.Add((nodeName, index, status, outputs)); }
        return Task.CompletedTask;
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
