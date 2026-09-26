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
    public List<(Guid RunId, Guid PipelineId, RunSubject? Subject)> RunsCreated { get; } = new();
    public List<Guid> StartTargetsAsked { get; } = new();
    public JsonElement? RunResult { get; private set; }

    public Task<PipelineGraph> LoadPipelineSubgraphAsync(Guid pipelineId, CancellationToken ct) => Task.FromResult(_graph);

    public Task<PipelineGraph> LoadStartSubgraphAsync(Guid targetId, CancellationToken ct)
    {
        StartTargetsAsked.Add(targetId);
        return Task.FromResult(_graph);
    }

    public Task CreateRunAsync(Guid runId, Guid pipelineId, CancellationToken ct, RunSubject? subject = null)
    {
        RunsCreated.Add((runId, pipelineId, subject));
        return Task.CompletedTask;
    }

    public Task SetNodeRunStatusAsync(Guid runId, Guid nodeId, string nodeName, string status, string? error, CancellationToken ct, int? index = null, int total = 0)
    {
        lock (NodeStatuses) NodeStatuses.Add((nodeName, status));
        return Task.CompletedTask;
    }

    public Task SetRunStatusAsync(Guid runId, string status, CancellationToken ct) { StatusUpdates.Add(status); return Task.CompletedTask; }
    public Task SetRunResultAsync(Guid runId, JsonElement result, CancellationToken ct) { RunResult = result.Clone(); return Task.CompletedTask; }
    public Task<bool> IsCancelRequestedAsync(Guid runId, CancellationToken ct) => Task.FromResult(CancelRequested(runId));

    public Task<NodeDispatchResult> DispatchAsync(string subdomain, JsonElement envelope, CancellationToken ct)
    {
        lock (Dispatched) { Dispatched.Add(subdomain); Envelopes.Add((subdomain, envelope.Clone())); }
        return Task.FromResult(OnDispatch(subdomain, envelope));
    }
}
