using System.Text.Json;
using vos.Service.Phloem.Model;

namespace vos.Service.Phloem.Execution;

// The raw reply from forwarding a node invocation through Mycelium's endpoint-forward.
public sealed record NodeDispatchResult(int StatusCode, string Body);

// Everything the executor needs from Mycelium, behind a seam so the orchestration logic is
// unit-testable without HTTP. The HTTP implementation is MyceliumGateway; run-persistence calls are
// best-effort (the executor never lets a persistence failure abort a run).
public interface IMyceliumGateway
{
    // Load the pipeline's structural closure (Pipeline, nodes, Connections, Services, prototypes,
    // Ports, wires) as a queryable graph.
    Task<PipelineGraph> LoadPipelineSubgraphAsync(Guid pipelineId, CancellationToken cancellationToken);

    // Create the PipelineRun Thing (id = runId), link it of the Pipeline and
    // is a PipelineRun.
    Task CreateRunAsync(Guid runId, Guid pipelineId, CancellationToken cancellationToken);

    // Upsert a NodeRun Thing and set its status — running before dispatch, then the terminal
    // status — on one Thing per key (deterministic id) so the SSE view sees a property change, not duplicate
    // Things (#5635). With index null this is the node's aggregate NodeRun (drives the
    // ring); with an index it's a per-item NodeRun of a fan-out (#5648), carrying index/total.
    Task SetNodeRunStatusAsync(Guid runId, Guid nodeId, string nodeName, string status, string? error, CancellationToken cancellationToken, int? index = null, int total = 0);

    // Update the run's status (drives the live SSE animation).
    Task SetRunStatusAsync(Guid runId, string status, CancellationToken cancellationToken);

    // Store the pipeline's published result (the Output boundary node's collected inputs) on the
    // PipelineRun Thing, so it persists in the model and streams over SSE like any other property (#5873).
    Task SetRunResultAsync(Guid runId, JsonElement result, CancellationToken cancellationToken);

    // True if the run's cancelRequested flag has been set (by Trellis). Polled between
    // dispatches for cooperative cancellation (#5635).
    Task<bool> IsCancelRequestedAsync(Guid runId, CancellationToken cancellationToken);

    // Invoke a node by forwarding its envelope through Mycelium to the Connection's subdomain.
    Task<NodeDispatchResult> DispatchAsync(string subdomain, JsonElement envelope, CancellationToken cancellationToken);
}
