using System.Text.Json;

namespace vos.Service.Phloem.Execution;

// What the model holds of one node in one run: its node run's status and the outputs it answered.
public sealed record NodeRecord(string Status, IReadOnlyDictionary<string, JsonElement> Outputs);

// A run as the model holds it, read back so a waiting run resumes from its record rather than from memory.
public sealed record RunRecord(Guid RunId, Guid PipelineId, JsonElement Parameters, IReadOnlyDictionary<Guid, NodeRecord> Nodes);

public sealed record VigilHolder(Guid RunId, Guid NodeId);

// A node run waiting for a Thing, with the vigil it holds; either may be missing from the model.
public sealed record HeldWait(Guid RunId, Guid NodeId, Guid? AwaitedId, Guid? VigilId);

public abstract record VigilPlacement
{
    public sealed record Placed(Guid VigilId) : VigilPlacement;

    public sealed record Refused(string Reason) : VigilPlacement;
}
