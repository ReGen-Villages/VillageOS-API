using System.Text.Json;

namespace vos.ManagedMicroservice.Phloem.Execution;

/// <summary>Terminal status of a run or a node.</summary>
public static class RunStatus
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
    public const string Cancelled = "cancelled";
    public const string Partial = "partial";
}

/// <summary>The outcome of a single node in a run.</summary>
public sealed record NodeRunResult(
    Guid NodeId,
    string Name,
    string Status,
    IReadOnlyDictionary<string, JsonElement> Outputs,
    string? Error);

/// <summary>The synchronous result Phloem returns to whoever spawned the run.</summary>
public sealed record PipelineRunResult(
    Guid RunId,
    Guid PipelineId,
    bool Success,
    IReadOnlyList<NodeRunResult> Nodes,
    string? Error)
{
    public static PipelineRunResult Failed(Guid runId, Guid pipelineId, string error, IReadOnlyList<NodeRunResult>? nodes = null) =>
        new(runId, pipelineId, false, nodes ?? Array.Empty<NodeRunResult>(), error);
}
