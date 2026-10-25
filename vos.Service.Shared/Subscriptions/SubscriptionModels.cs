using System.Text.Json;
using System.Text.Json.Serialization;

namespace vos.Service.Shared.Subscriptions;

public sealed class SubscriptionSelector
{
    // Cover the whole model plus all future objects.
    public bool All { get; set; }
    public List<Guid>? Ids { get; set; }
    public List<string>? Names { get; set; }
    public List<string>? Types { get; set; }

    // Each a list of role Thing identifiers. BoundTypes selects the
    // archetype a role binds and every Thing that `is` it; BoundArchetypes selects the bound Thing alone, which may be
    // a predicate or a single Thing as well as an archetype. Neither carries the role Thing or its `binds`
    // relationship: a reader that has to tell which bound Thing plays which role asks for the role Things by id and
    // follows RoleBindings.FollowBindings.
    public List<Guid>? BoundTypes { get; set; }
    public List<Guid>? BoundArchetypes { get; set; }
    public List<TraverseRule>? Traverse { get; set; }
    public bool IncludeIsAncestors { get; set; } = true;
    public bool IncludeRelationships { get; set; } = true;

    // Which relationships the snapshot carries and the stream admits. Without rules, IncludeRelationships
    // carries every active relationship touching a selected Thing in both directions — for a long-lived Thing,
    // every relationship ever made against it. Each rule names a direction and a predicate by name or by flag
    // (neither means every predicate); only the relationships a selected Thing holds that way travel. Rules beside
    // IncludeRelationships = false are refused by the broker as undecided.
    public List<RelationshipRule>? Relationships { get; set; }
}

public sealed class RelationshipRule
{
    public string Predicate { get; set; } = "";

    // By a flag the predicate carries, or by the role Thing that binds it, rather than by the name a model
    // chose. Set one of the three; the broker refuses a rule that gives more than one.
    public string? PredicateFlag { get; set; }
    public Guid? PredicateRole { get; set; }
    public string Direction { get; set; } = "outgoing"; // outgoing | incoming | both
}

public sealed class TraverseRule
{
    public string Predicate { get; set; } = "";

    // The predicate to follow, by a flag it carries or by the role Thing that binds it, rather than by the
    // name a model chose. Set one of the three: the broker refuses a rule that gives more than one, because
    // preferring one silently would make a typo in another read as an ordinary empty result.
    public string? PredicateFlag { get; set; }
    public Guid? PredicateRole { get; set; }
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
// SnapshotValues.StatedValue rather than either directly.
//
// Relationships is the closure's relationships incident to the Thing, which only a snapshot can say anything
// about; the same shape on the change stream carries none, since a Thing's relationships arrive as events.
public sealed record SnapshotThing(
    Guid Id,
    string? Name,
    bool IsArchetype,
    Dictionary<string, SnapshotProperty> Properties,
    Dictionary<string, InheritedPropertySet>? InheritedOverrides,
    string[] States,
    Guid[]? Relationships);

public sealed record SnapshotRelationship(
    Guid Id,
    string? Name,
    Guid SubjectId,
    Guid PredicateId,
    Guid TargetId,
    Dictionary<string, SnapshotProperty> Properties,
    Dictionary<string, InheritedPropertySet>? InheritedOverrides,
    string[] States);

// Sequence is the commit sequence (the SSE event id) used for Last-Event-ID resume. An entry or a
// departure — a Thing or relationship a following subscription brought in or let go — rides beside the change
// that caused it and carries no id, so its Sequence is 0; the change after it carries the sequence.
//
// A Thing or a relationship met for the first time arrives whole under Thing or Relationship, in the
// snapshot's shape, so a consumer applies it without a read.
public sealed record ModelChangeEvent
{
    public long Sequence { get; init; }
    public string Kind { get; init; } = "";
    public Guid EntityId { get; init; }
    public string? PropertyName { get; init; }
    public JsonElement? Value { get; init; }
    public SnapshotThing? Thing { get; init; }
    public SnapshotRelationship? Relationship { get; init; }

    [JsonIgnore] public bool IsPropertyChange => Kind is "PropertyChanged" or "RelationshipPropertyChanged";
}
