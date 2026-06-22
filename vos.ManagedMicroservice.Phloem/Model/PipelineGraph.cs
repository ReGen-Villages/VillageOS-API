using System.Text.Json;

namespace vos.ManagedMicroservice.Phloem.Model;

/// <summary>A Thing in the loaded pipeline subgraph: identity, name, and unwrapped property values.</summary>
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

/// <summary>A relationship: subject –predicate→ target, with unwrapped edge property values.</summary>
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

/// <summary>
/// The pipeline subgraph loaded from a Mycelium subscription snapshot: Things + Relationships, with the
/// model traversals the orchestrator needs. <c>is</c> and <c>has</c> are the built-in generic predicates
/// and are matched by name; every other predicate (e.g. a pipeline wire) is identified by its predicate
/// Thing's archetype via <see cref="IsOfType"/> — never by predicate-name string.
/// </summary>
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

    /// <summary>The Thing acting as a relationship's predicate (predicates are Things).</summary>
    public GraphThing? Predicate(GraphRelationship rel) => Thing(rel.PredicateId);

    /// <summary>True if <paramref name="thing"/> is (transitively, via <c>is</c>) of the named archetype.</summary>
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

    /// <summary>Targets reached from <paramref name="subject"/> via a predicate matched by name (use for the
    /// built-in <c>is</c>/<c>has</c>).</summary>
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

    /// <summary>Subjects pointing at <paramref name="target"/> via a predicate matched by name (e.g. the
    /// Connection that <c>has</c> a Service).</summary>
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

    /// <summary>Outgoing relationships from <paramref name="subject"/> whose predicate <c>is</c> the given
    /// archetype — the no-hardcoded-predicate way to find wires (predicate <c>is PipelineWire</c>).</summary>
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
