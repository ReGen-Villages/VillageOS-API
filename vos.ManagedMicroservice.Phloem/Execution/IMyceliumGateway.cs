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

    /// <summary>Record a node's outcome as a NodeRun Thing linked <c>has</c> from the run.</summary>
    Task PersistNodeRunAsync(Guid runId, NodeRunResult node, CancellationToken cancellationToken);

    /// <summary>Update the run's terminal status (drives the live SSE animation in #5635).</summary>
    Task SetRunStatusAsync(Guid runId, string status, CancellationToken cancellationToken);

    /// <summary>Invoke a node by forwarding its envelope through Mycelium to the Connection's subdomain.</summary>
    Task<NodeDispatchResult> DispatchAsync(string subdomain, JsonElement envelope, CancellationToken cancellationToken);
}
