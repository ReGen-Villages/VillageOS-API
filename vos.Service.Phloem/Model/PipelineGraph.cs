using System.Text.Json;

namespace vos.Service.Phloem.Model;

// A Thing in the loaded pipeline subgraph: identity, name, and unwrapped property values.
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
}

// A relationship: subject –predicate→ target, with unwrapped edge property values.
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
// model traversals the orchestrator needs. is and has are the built-in generic predicates
// and are matched by name; every other predicate (e.g. a pipeline wire) is identified by its predicate
// Thing's archetype via IsOfType — never by predicate-name string.
public sealed class PipelineGraph
{
    private const string IsPredicate = "is";

    private readonly Dictionary<Guid, GraphThing> _things;
    private readonly List<GraphRelationship> _relationships;

    public PipelineGraph(IReadOnlyDictionary<Guid, GraphThing> things, IReadOnlyList<GraphRelationship> relationships)
    {
        _things = new Dictionary<Guid, GraphThing>(things);
        _relationships = relationships.ToList();
    }

    public IReadOnlyCollection<GraphThing> Things => _things.Values;

    public GraphThing? Thing(Guid id) => _things.TryGetValue(id, out var t) ? t : null;

    // The Thing acting as a relationship's predicate (predicates are Things).
    public GraphThing? Predicate(GraphRelationship rel) => Thing(rel.PredicateId);

    // True if thing is (transitively, via is) of the named archetype.
    public bool IsOfType(GraphThing thing, string archetypeName) =>
        IsOfType(thing, archetypeName, new HashSet<Guid>());

    private bool IsOfType(GraphThing thing, string archetypeName, HashSet<Guid> seen)
    {
        if (!seen.Add(thing.Id)) return false;
        if (string.Equals(thing.Name, archetypeName, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var parent in OutgoingTargets(thing, IsPredicate))
            if (IsOfType(parent, archetypeName, seen)) return true;
        return false;
    }

    // Targets reached from subject via a predicate matched by name (use for the
    // built-in is/has).
    public IEnumerable<GraphThing> OutgoingTargets(GraphThing subject, string predicateName)
    {
        foreach (var rel in _relationships)
        {
            if (rel.SubjectId != subject.Id) continue;
            var predicate = Thing(rel.PredicateId);
            if (predicate != null && string.Equals(predicate.Name, predicateName, StringComparison.OrdinalIgnoreCase)
                && Thing(rel.TargetId) is { } target)
                yield return target;
        }
    }

    // Subjects pointing at target via a predicate matched by name (e.g. the
    // Connection that has a Service).
    public IEnumerable<GraphThing> IncomingSubjects(GraphThing target, string predicateName)
    {
        foreach (var rel in _relationships)
        {
            if (rel.TargetId != target.Id) continue;
            var predicate = Thing(rel.PredicateId);
            if (predicate != null && string.Equals(predicate.Name, predicateName, StringComparison.OrdinalIgnoreCase)
                && Thing(rel.SubjectId) is { } subject)
                yield return subject;
        }
    }

    // Outgoing relationships from subject whose predicate is the given
    // archetype — the no-hardcoded-predicate way to find wires (predicate is PipelineWire).
    public IEnumerable<GraphRelationship> OutgoingByPredicateType(GraphThing subject, string predicateArchetype)
    {
        foreach (var rel in _relationships)
        {
            if (rel.SubjectId != subject.Id) continue;
            if (Thing(rel.PredicateId) is { } predicate && IsOfType(predicate, predicateArchetype))
                yield return rel;
        }
    }
}
