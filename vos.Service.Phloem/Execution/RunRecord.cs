using System.Text.Json;

namespace vos.Service.Phloem.Execution;

// One call of a node's service as the model holds it: when it ran on the model clock and how it ended.
public sealed record AttemptRecord(int Number, DateTimeOffset StartedUtc, DateTimeOffset FinishedUtc, string Status, string? Error);

// What the model holds of one node in one run: its node run's status, the outputs it answered and the attempts it made.
public sealed record NodeRecord(string Status, IReadOnlyDictionary<string, JsonElement> Outputs, IReadOnlyList<AttemptRecord>? Attempts = null)
{
    public IReadOnlyList<AttemptRecord> AttemptsMade => Attempts ?? [];
}

// A run as the model holds it, read back so a waiting run resumes from its record rather than from memory.
public sealed record RunRecord(Guid RunId, Guid PipelineId, JsonElement Parameters, IReadOnlyDictionary<Guid, NodeRecord> Nodes, bool Rehearsal = false);

public sealed record VigilHolder(Guid RunId, Guid NodeId);

// A node run waiting for a Thing, with the vigil it holds; either may be missing from the model.
public sealed record HeldWait(Guid RunId, Guid NodeId, Guid? AwaitedId, Guid? VigilId);

// A node run paused between attempts; one of a fan-out's items when PerItem.
public sealed record TryingNodeRun(Guid RunId, Guid NodeId, bool PerItem);

public abstract record VigilPlacement
{
    public sealed record Placed(Guid VigilId) : VigilPlacement;

    public sealed record Refused(string Reason) : VigilPlacement;
}
