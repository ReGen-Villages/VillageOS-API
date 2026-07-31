using System.Text.Json;

namespace vos.Service.Phloem.Execution;

// Terminal status of a run or a node.
public static class RunStatus
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
    public const string Cancelled = "cancelled";
    public const string Partial = "partial";
}

// The outcome of a single node in a run.
public sealed record NodeRunResult(
    Guid NodeId,
    string Name,
    string Status,
    IReadOnlyDictionary<string, JsonElement> Outputs,
    string? Error);

// The synchronous result Phloem returns to whoever spawned the run. Result is the
// pipeline's published output — the Output boundary node's collected inputs — or null when the pipeline has
// no Output node or did not succeed (#5873).
public sealed record PipelineRunResult(
    Guid RunId,
    Guid PipelineId,
    bool Success,
    IReadOnlyList<NodeRunResult> Nodes,
    string? Error,
    JsonElement? Result = null)
{
    public static PipelineRunResult Failed(Guid runId, Guid pipelineId, string error, IReadOnlyList<NodeRunResult>? nodes = null) =>
        new(runId, pipelineId, false, nodes ?? Array.Empty<NodeRunResult>(), error);
}
