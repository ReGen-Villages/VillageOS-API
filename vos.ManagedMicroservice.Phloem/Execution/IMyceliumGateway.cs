using System.Text.Json;
using vos.ManagedMicroservice.Phloem.Model;

namespace vos.ManagedMicroservice.Phloem.Execution;

/// <summary>The raw reply from forwarding a node invocation through Mycelium's endpoint-forward.</summary>
public sealed record NodeDispatchResult(int StatusCode, string Body);

/// <summary>Everything the executor needs from Mycelium, behind a seam so the orchestration logic is
/// unit-testable without HTTP. The HTTP implementation is <c>MyceliumGateway</c>; run-persistence calls are
/// best-effort (the executor never lets a persistence failure abort a run).</summary>
public interface IMyceliumGateway
{
    /// <summary>Load the pipeline's structural closure (Pipeline, nodes, Connections, Services, prototypes,
    /// Ports, wires) as a queryable graph.</summary>
    Task<PipelineGraph> LoadPipelineSubgraphAsync(Guid pipelineId, CancellationToken cancellationToken);

    /// <summary>Create the PipelineRun Thing (id = <paramref name="runId"/>), link it <c>of</c> the Pipeline and
    /// <c>is</c> a PipelineRun.</summary>
    Task CreateRunAsync(Guid runId, Guid pipelineId, CancellationToken cancellationToken);

    /// <summary>Upsert a NodeRun Thing and set its status — <c>running</c> before dispatch, then the terminal
    /// status — on one Thing per key (deterministic id) so the SSE view sees a property change, not duplicate
    /// Things (#5635). With <paramref name="index"/> null this is the node's <b>aggregate</b> NodeRun (drives the
    /// ring); with an index it's a <b>per-item</b> NodeRun of a fan-out (#5648), carrying <c>index</c>/<c>total</c>.</summary>
    Task SetNodeRunStatusAsync(Guid runId, Guid nodeId, string nodeName, string status, string? error, CancellationToken cancellationToken, int? index = null, int total = 0);

    /// <summary>Update the run's status (drives the live SSE animation).</summary>
    Task SetRunStatusAsync(Guid runId, string status, CancellationToken cancellationToken);

    /// <summary>Store the pipeline's published result (the Output boundary node's collected inputs) on the
    /// PipelineRun Thing, so it persists in the model and streams over SSE like any other property (#5873).</summary>
    Task SetRunResultAsync(Guid runId, JsonElement result, CancellationToken cancellationToken);

    /// <summary>True if the run's <c>cancelRequested</c> flag has been set (by Trellis). Polled between
    /// dispatches for cooperative cancellation (#5635).</summary>
    Task<bool> IsCancelRequestedAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>Invoke a node by forwarding its envelope through Mycelium to the Connection's subdomain.</summary>
    Task<NodeDispatchResult> DispatchAsync(string subdomain, JsonElement envelope, CancellationToken cancellationToken);
}
