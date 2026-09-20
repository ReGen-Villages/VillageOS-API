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

    Task CreateRunAsync(Guid runId, Guid pipelineId, CancellationToken cancellationToken);

    // Upsert a NodeRun Thing and set its status — running before dispatch, then the terminal
    // status — on one Thing per key (deterministic id) so the SSE view sees a property change, not duplicate
    // Things. With index null this is the node's aggregate NodeRun (drives the
    // ring); with an index it's a per-item NodeRun of a fan-out, carrying index/total.
    Task SetNodeRunStatusAsync(Guid runId, Guid nodeId, string nodeName, string status, string? error, CancellationToken cancellationToken, int? index = null, int total = 0);

    Task SetRunStatusAsync(Guid runId, string status, CancellationToken cancellationToken);

    // Store the pipeline's published result (the Output boundary node's collected inputs) on the
    // PipelineRun Thing, so it persists in the model and streams over SSE like any other property.
    Task SetRunResultAsync(Guid runId, JsonElement result, CancellationToken cancellationToken);

    // True if the run's cancelRequested flag has been set (by Trellis). Polled between
    // dispatches for cooperative cancellation.
    Task<bool> IsCancelRequestedAsync(Guid runId, CancellationToken cancellationToken);

    Task<NodeDispatchResult> DispatchAsync(string subdomain, JsonElement envelope, CancellationToken cancellationToken);
}
