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

    // Every Thing that `is` an archetype carrying one of these flags. Reaches Things the traversal cannot,
    // which is what a reader needs when it is about to create the edge the traversal would have followed.
    public List<string>? MarkedTypes { get; set; }

    // The Things carrying one of these flags and none of their members. What a caller wants when it needs
    // an id to write an edge with — a predicate, or an archetype to point `is` at — and would otherwise
    // have to take the whole membership to reach it.
    public List<string>? MarkedArchetypes { get; set; }
    public List<TraverseRule>? Traverse { get; set; }
    public bool IncludeIsAncestors { get; set; } = true;
    public bool IncludeRelationships { get; set; } = true;
}

public sealed class TraverseRule
{
    public string Predicate { get; set; } = "";

    // The predicate to follow, by a flag it carries rather than by the name a model chose (#6551). Set
    // this or Predicate, never both: the broker refuses a rule that gives both, because preferring one
    // silently would make a typo in the other read as an ordinary empty result.
    public string? PredicateFlag { get; set; }
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

// What a Thing states for a name one of its type ancestors declares, kept apart from its own properties
// because the snapshot never shadows one with the other. `Inherited` nests one set per further ancestor,
// for a name declared further up the chain than the type the Thing directly `is`.
public sealed record InheritedPropertySet(
    string? SourceName,
    Dictionary<string, SnapshotProperty> Properties,
    Dictionary<string, InheritedPropertySet>? Inherited);

// IsArchetype tells a type from a member of one. Read it before walking the members of a type, or
// the type itself comes back among them — nothing else in the payload separates the two.
//
// InheritedOverrides is named as Mycelium serializes it. A member named anything else binds to nothing,
// and since that is where every value a Thing states over its archetype's declaration is carried, a
// reader of Properties alone finds each of them absent. Read both through
// <see cref="SnapshotValues.StatedValue(SnapshotThing, string)"/> rather than either directly.
public sealed record SnapshotThing(
    Guid Id,
    string? Name,
    bool IsArchetype,
    Dictionary<string, SnapshotProperty> Properties,
    Dictionary<string, InheritedPropertySet>? InheritedOverrides,
    string[] States,
    Guid[] Relationships);

public sealed record SnapshotRelationship(
    Guid Id,
    string? Name,
    Guid SubjectId,
    Guid PredicateId,
    Guid TargetId,
    Dictionary<string, SnapshotProperty> Properties,
    Dictionary<string, InheritedPropertySet>? InheritedOverrides,
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
