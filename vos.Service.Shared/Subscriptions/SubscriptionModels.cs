using System.Text.Json;
using System.Text.Json.Serialization;

namespace vos.Service.Shared.Subscriptions;

// Selector for POST /api/subscriptions resolving the object closure to snapshot and stream.
public sealed class SubscriptionSelector
{
    // Cover the whole model plus all future objects.
    public bool All { get; set; }
    public List<Guid>? Ids { get; set; }
    public List<string>? Names { get; set; }
    public List<string>? Types { get; set; }
    public List<TraverseRule>? Traverse { get; set; }
    public bool IncludeIsAncestors { get; set; } = true;
    public bool IncludeRelationships { get; set; } = true;
}

public sealed class TraverseRule
{
    public string Predicate { get; set; } = "";
    public string Direction { get; set; } = "outgoing"; // outgoing | incoming | both
    public int Depth { get; set; } = 1;
}

public sealed record SubscribeResult(Guid SubscriptionId, long Watermark, SnapshotDocument Snapshot);

public sealed record AddObjectsResult(long Watermark, SnapshotDocument Snapshot);

public sealed record SnapshotDocument(
    long Watermark,
    List<SnapshotThing> Things,
    List<SnapshotRelationship> Relationships);

public sealed record SnapshotProperty(JsonElement Value, string? TypeInfo, string? Mode);

// Properties inherited from one type ancestor, kept separate from own properties (no shadowing).
public sealed record InheritedPropertySet(
    string? SourceName,
    Dictionary<string, SnapshotProperty> Properties);

// IsArchetype tells a type from a member of one. Read it before walking the members of a type, or
// the type itself comes back among them — nothing else in the payload separates the two.
public sealed record SnapshotThing(
    Guid Id,
    string? Name,
    bool IsArchetype,
    Dictionary<string, SnapshotProperty> Properties,
    Dictionary<string, InheritedPropertySet> InheritedProperties,
    string[] States,
    Guid[] Relationships);

public sealed record SnapshotRelationship(
    Guid Id,
    string? Name,
    Guid SubjectId,
    Guid PredicateId,
    Guid TargetId,
    Dictionary<string, SnapshotProperty> Properties,
    Dictionary<string, InheritedPropertySet> InheritedProperties,
    string[] States);

// Sequence is the commit sequence (the SSE event id) used for Last-Event-ID resume.
public sealed record ModelChangeEvent
{
    public long Sequence { get; init; }
    public string Kind { get; init; } = "";
    public Guid EntityId { get; init; }
    public string? PropertyName { get; init; }
    public JsonElement? Value { get; init; }

    [JsonIgnore] public bool IsPropertyChange => Kind is "PropertyChanged" or "RelationshipPropertyChanged";
}
