using System.Text.Json;
using vos.Service.Phloem.Model;

namespace vos.Service.Phloem.Execution;

public sealed record NodeDispatchResult(int StatusCode, string Body);

// Everything the executor needs from Mycelium, behind a seam so the orchestration logic is
// unit-testable without HTTP. The HTTP implementation is MyceliumGateway; run-persistence calls are
// best-effort (the executor never lets a persistence failure abort a run).
public interface IMyceliumGateway
{
    Task<PipelineGraph> LoadPipelineSubgraphAsync(Guid pipelineId, CancellationToken cancellationToken);

    // What a dispatched relationship's target is, with the start nodes standing for it and the pipelines
    // they belong to or it reaches — enough for PipelineStart to say which pipeline it starts.
    Task<PipelineGraph> LoadStartSubgraphAsync(Guid targetId, CancellationToken cancellationToken);

    // The predicate a relationship was written along, or none when the model does not hold it.
    Task<Guid?> PredicateOfAsync(Guid relationshipId, CancellationToken cancellationToken);

    // A message the run leaves for an external system: written, held by the run, then related to the system
    // along the connection it is told through, which the broker delivers.
    Task SendMessageAsync(Guid runId, Telling telling, JsonElement payload, CancellationToken cancellationToken);

    // The subject, when the run has one, is related to the run along the predicate the model marks for it.
    // The request that started it, when the broker named one, is the identifier of its request-log entry.
    // The parameters are kept on the run as given, so a trace and a resumed run read what it was asked.
    Task CreateRunAsync(Guid runId, Guid pipelineId, CancellationToken cancellationToken, RunSubject? subject = null, Guid? requestId = null,
        JsonElement? parameters = null);

    // Upsert a NodeRun Thing and set its status — running before dispatch, then the terminal
    // status — on one Thing per key (deterministic id) so the SSE view sees a property change, not duplicate
    // Things. With index null this is the node's aggregate NodeRun (drives the
    // ring); with an index it's a per-item NodeRun of a fan-out, carrying index/total. A terminal status
    // carries the outputs, which a resumed run reads back from the model.
    Task SetNodeRunStatusAsync(Guid runId, Guid nodeId, string nodeName, string status, string? error, CancellationToken cancellationToken, int? index = null, int total = 0,
        IReadOnlyDictionary<string, JsonElement>? outputs = null);

    // The node run is related to every Thing the node answered under an out port marked as producing one,
    // along the predicate the model marks for it, so a trace is a read of the Thing's own relationships.
    Task RelateProducedAsync(Guid runId, Guid nodeId, int? index, IReadOnlyList<Guid> producedThingIds, CancellationToken cancellationToken);

    // The reason travels with a failure, so a run started by a state entry — one nobody awaits — still
    // says in the model why it failed.
    Task SetRunStatusAsync(Guid runId, string status, CancellationToken cancellationToken, string? error = null);

    // Store the pipeline's published result (the Output boundary node's collected inputs) on the
    // PipelineRun Thing, so it persists in the model and streams over SSE like any other property.
    Task SetRunResultAsync(Guid runId, JsonElement result, CancellationToken cancellationToken);

    // True if the run's cancelRequested flag has been set (by Trellis). Polled between
    // dispatches for cooperative cancellation.
    Task<bool> IsCancelRequestedAsync(Guid runId, CancellationToken cancellationToken);

    Task<NodeDispatchResult> DispatchAsync(string subdomain, JsonElement envelope, CancellationToken cancellationToken);
}
