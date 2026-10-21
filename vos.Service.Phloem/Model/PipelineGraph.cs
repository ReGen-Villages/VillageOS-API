using System.Text.Json;

namespace vos.Service.Phloem.Model;

public sealed class GraphThing
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, JsonElement> Properties { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

    public string? PropertyString(string name) =>
        Properties.TryGetValue(name, out var v)
            ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()
            : null;

    // A template declares a port's marks as booleans and the page writes them as text, so both are read as one.
    public bool CarriesFlag(string name) =>
        Properties.TryGetValue(name, out var value) && Truth.IsTrue(value);
}

public static class Truth
{
    public static bool IsTrue(JsonElement value) =>
        value.ValueKind == JsonValueKind.True
        || (value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase));
}

public sealed class GraphRelationship
{
    public Guid Id { get; init; }
    public Guid SubjectId { get; init; }
    public Guid PredicateId { get; init; }
    public Guid TargetId { get; init; }
    public IReadOnlyDictionary<string, JsonElement> Properties { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

    public string? PropertyString(string name) =>
        Properties.TryGetValue(name, out var v)
            ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()
            : null;
}

// The pipeline subgraph loaded from a Mycelium subscription snapshot: Things + Relationships, with the
// model traversals the orchestrator needs. is, has and binds are the built-in generic predicates and are matched
// by name; everything else is reached through the role Thing that binds it, so a model may name its
// archetypes and its wire predicates whatever it likes.
public sealed class PipelineGraph
{
    private const string IsPredicate = "is";

    private const string HasPredicate = "has";

    private const string BindsPredicate = ModelNames.Binds;

    private readonly Dictionary<Guid, GraphThing?> _bound = new();

    private static readonly IReadOnlyList<GraphRelationship> NoRelationships = Array.Empty<GraphRelationship>();

    private readonly Dictionary<Guid, GraphThing> _things;
    private readonly Dictionary<Guid, List<GraphRelationship>> _bySubject = new();
    private readonly Dictionary<Guid, List<GraphRelationship>> _byTarget = new();

    // Indexed by each end once, because the builder walks out of every node, port and connection, and a
    // walk that scanned every relationship cost a third of a drawing's build.
    public PipelineGraph(IReadOnlyDictionary<Guid, GraphThing> things, IReadOnlyList<GraphRelationship> relationships)
    {
        _things = new Dictionary<Guid, GraphThing>(things);
        foreach (var relationship in relationships)
        {
            Index(_bySubject, relationship.SubjectId, relationship);
            Index(_byTarget, relationship.TargetId, relationship);
        }
    }

    private static void Index(Dictionary<Guid, List<GraphRelationship>> index, Guid end, GraphRelationship relationship)
    {
        if (!index.TryGetValue(end, out var list)) index[end] = list = new List<GraphRelationship>();
        list.Add(relationship);
    }

    private IReadOnlyList<GraphRelationship> From(GraphThing subject) =>
        _bySubject.TryGetValue(subject.Id, out var list) ? list : NoRelationships;

    private IReadOnlyList<GraphRelationship> Into(GraphThing target) =>
        _byTarget.TryGetValue(target.Id, out var list) ? list : NoRelationships;

    // The relationships themselves, for a read that needs their identifiers or an end the snapshot does not hold.
    public IEnumerable<GraphRelationship> RelationshipsAlong(GraphThing subject, Role role) =>
        Bound(role) is { } predicate ? From(subject).Where(rel => rel.PredicateId == predicate.Id) : [];

    public IEnumerable<GraphRelationship> RelationshipsInto(GraphThing target, string predicateName) =>
        Into(target).Where(rel => Thing(rel.PredicateId) is { } predicate
            && string.Equals(predicate.Name, predicateName, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyCollection<GraphThing> Things => _things.Values;

    public GraphThing? Thing(Guid id) => _things.TryGetValue(id, out var t) ? t : null;

    // The Thing a role binds, or null when the model binds none. A snapshot asked for the role Things carries them
    // whether or not the pipeline in it uses one, so a null here says the model binds that role to nothing.
    public GraphThing? Bound(Role role)
    {
        if (_bound.TryGetValue(role.Id, out var known)) return known;

        GraphThing? bound = null;
        if (Thing(role.Id) is { } roleThing)
        {
            var played = OutgoingTargets(roleThing, BindsPredicate).ToList();
            if (played.Count > 1)
                throw new PipelineModelException(
                    $"The role {role.Word} binds {played.Count} Things: {string.Join(", ", played.Select(thing => $"'{thing.Name}'"))}. "
                    + "A role binds one Thing.");
            bound = played.FirstOrDefault();
        }
        return _bound[role.Id] = bound;
    }

    // True when the Thing `is`, directly or through others, the Thing the role binds. An archetype does not play
    // its own role, so the walk starts above the Thing.
    public bool IsOf(GraphThing thing, Role role) =>
        Bound(role) is { } archetype && IsBeneath(thing, archetype.Id, new HashSet<Guid> { thing.Id });

    private bool IsBeneath(GraphThing thing, Guid archetypeId, HashSet<Guid> seen)
    {
        foreach (var parent in OutgoingTargets(thing, IsPredicate))
        {
            if (!seen.Add(parent.Id)) continue;
            if (parent.Id == archetypeId) return true;
            if (IsBeneath(parent, archetypeId, seen)) return true;
        }
        return false;
    }

    // Targets reached from subject via a predicate matched by name (use for the
    // built-in is/has).
    public IEnumerable<GraphThing> OutgoingTargets(GraphThing subject, string predicateName)
    {
        foreach (var rel in From(subject))
        {
            var predicate = Thing(rel.PredicateId);
            if (predicate != null && string.Equals(predicate.Name, predicateName, StringComparison.OrdinalIgnoreCase)
                && Thing(rel.TargetId) is { } target)
                yield return target;
        }
    }

    // Subjects reaching the target via a predicate matched by name (use for the built-in is/has).
    public IEnumerable<GraphThing> IncomingSubjects(GraphThing target, string predicateName)
    {
        foreach (var rel in Into(target))
        {
            var predicate = Thing(rel.PredicateId);
            if (predicate != null && string.Equals(predicate.Name, predicateName, StringComparison.OrdinalIgnoreCase)
                && Thing(rel.SubjectId) is { } subject)
                yield return subject;
        }
    }

    // Targets reached from the subject along the predicate a role binds — the predicates the platform dispatches
    // and records along, as against a wire's predicate, which `is` the wire archetype.
    public IEnumerable<GraphThing> OutgoingAlong(GraphThing subject, Role role)
    {
        if (Bound(role) is not { } predicate) yield break;
        foreach (var rel in From(subject))
            if (rel.PredicateId == predicate.Id && Thing(rel.TargetId) is { } target)
                yield return target;
    }

    public IEnumerable<GraphThing> IncomingAlong(GraphThing target, Role role)
    {
        if (Bound(role) is not { } predicate) yield break;
        foreach (var rel in Into(target))
            if (rel.PredicateId == predicate.Id && Thing(rel.SubjectId) is { } subject)
                yield return subject;
    }

    // Outgoing relationships whose predicate Thing `is` what the role binds — how a wire is found without naming
    // either the predicate or the archetype it comes from.
    public IEnumerable<GraphRelationship> OutgoingByPredicateOf(GraphThing subject, Role role)
    {
        foreach (var rel in From(subject))
            if (Thing(rel.PredicateId) is { } predicate && IsOf(predicate, role))
                yield return rel;
    }

    // Things the subject has that play the held role, each with the Thing it points at that plays the target
    // role — how a wire held as a Thing is found, and where it goes, without naming either predicate.
    //
    // A wire that points at no such Thing is skipped rather than reported: it is half-drawn, and the shape
    // this reads is one an editor writes a piece at a time.
    public IEnumerable<(GraphThing Held, GraphThing Target)> HeldThingsOf(GraphThing subject, Role heldRole, Role targetRole)
    {
        foreach (var held in OutgoingTargets(subject, HasPredicate))
        {
            if (!IsOf(held, heldRole)) continue;
            foreach (var rel in From(held))
            {
                if (Thing(rel.TargetId) is { } target && IsOf(target, targetRole))
                {
                    yield return (held, target);
                    break;
                }
            }
        }
    }
}
