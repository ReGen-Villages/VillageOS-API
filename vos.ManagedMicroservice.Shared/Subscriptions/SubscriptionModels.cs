using System.Text.Json;
using System.Text.Json.Serialization;

namespace vos.ManagedMicroservice.Shared.Subscriptions;

/// <summary>
/// Selector sent to <c>POST /api/subscriptions</c> to resolve the object closure a
/// microservice wants delivered at startup and streamed thereafter. Mirrors the
/// Mycelium-side request shape (see VillageOS docs/SNAPSHOT_SUBSCRIPTIONS.md).
/// </summary>
public sealed class SubscriptionSelector
{
    /// <summary>Cover the whole model + all future objects (e.g. a GUI holding the entire model).</summary>
    public bool All { get; set; }
    public List<Guid>? Ids { get; set; }
    public List<string>? Names { get; set; }
    public List<string>? Types { get; set; }
    public List<TraverseRule>? Traverse { get; set; }
    public bool IncludeIsAncestors { get; set; } = true;
    public bool IncludeRelationships { get; set; } = true;
}

/// <summary>One relationship-traversal expansion rule. Direction is a string the server parses.</summary>
public sealed class TraverseRule
{
    public string Predicate { get; set; } = "";
    public string Direction { get; set; } = "outgoing"; // outgoing | incoming | both
    public int Depth { get; set; } = 1;
}

/// <summary>Result of <c>POST /api/subscriptions</c>: the registration id, the watermark, and the snapshot.</summary>
public sealed record SubscribeResult(Guid SubscriptionId, long Watermark, SnapshotDocument Snapshot);

/// <summary>Result of extending a subscription via <c>POST /api/subscriptions/{id}/objects</c>: an incremental snapshot of the added objects.</summary>
public sealed record AddObjectsResult(long Watermark, SnapshotDocument Snapshot);

/// <summary>The startup snapshot — full objects as of <see cref="Watermark"/> (a commit sequence).</summary>
public sealed record SnapshotDocument(
    long Watermark,
    List<SnapshotThing> Things,
    List<SnapshotRelationship> Relationships);

/// <summary>A property value in a snapshot: the raw JSON value plus its VOS type tag.</summary>
public sealed record SnapshotProperty(JsonElement Value, string? TypeInfo, string? Mode);

/// <summary>Properties inherited from one type ancestor — kept separate from own properties (no shadowing).</summary>
public sealed record InheritedPropertySet(
    string? SourceName,
    Dictionary<string, SnapshotProperty> Properties);

public sealed record SnapshotThing(
    Guid Id,
    string? Name,
    Dictionary<string, SnapshotProperty> Properties,            // OWN
    Dictionary<string, InheritedPropertySet> InheritedProperties, // INHERITED, by source type
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

/// <summary>
/// One live change delivered over the SSE stream. <see cref="Sequence"/> is the commit
/// sequence (the SSE event id) used for Last-Event-ID resume; the rest comes from the event data.
/// </summary>
public sealed record ModelChangeEvent
{
    public long Sequence { get; init; }
    public string Kind { get; init; } = "";
    public Guid EntityId { get; init; }
    public string? PropertyName { get; init; }
    public JsonElement? Value { get; init; }

    [JsonIgnore] public bool IsPropertyChange => Kind is "PropertyChanged" or "RelationshipPropertyChanged";
}
