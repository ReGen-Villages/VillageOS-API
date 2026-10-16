using System.Text.Json;

namespace vos.Service.Phloem.Execution;

public static class RunStatus
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
    public const string Cancelled = "cancelled";
    public const string Partial = "partial";
    // Every wire into the node was settled and none carried a value: the branch was not the one taken.
    public const string NotTaken = "notTaken";
    // The node's service has answered and the run waits for the Thing it names to enter the node's state.
    public const string Waiting = "waiting";
    // A call never reached the node's service and the node pauses before the next attempt.
    public const string Trying = "trying";
    // The node's trying ended without an answer and its subject went to the drawing the node names.
    public const string HandedOver = "handedOver";
    // The drawing was judged unable to run, so nothing ran.
    public const string Refused = "refused";
    // A rehearsal reached its ends; an end that would have sent or started something did neither.
    public const string Rehearsed = "rehearsed";
    // A rehearsal stopped at a node whose service does not rehearse, or that waits.
    public const string CannotRehearse = "cannotRehearse";

    public static bool IsFinished(string status) => status is not (Running or Waiting or Trying);
}

public sealed record NodeRunResult(
    Guid NodeId,
    string Name,
    string Status,
    IReadOnlyDictionary<string, JsonElement> Outputs,
    string? Error,
    AwaitedThing? Awaits = null);

public sealed record AwaitedThing(Guid Id, string Name);

public enum ResumeOutcome { Resumed, AlreadyResumed, NotHeld }

// The synchronous result Phloem returns to whoever spawned the run. Result is the
// pipeline's published output — the Output boundary node's collected inputs — or null when the pipeline has
// no Output node or did not succeed.
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
