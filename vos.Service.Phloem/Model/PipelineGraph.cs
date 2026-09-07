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

    /// <summary>True when the Thing carries this flag set to true. A model says a Thing plays a role by
    /// marking it; absent, false, and a non-boolean all mean it does not.</summary>
    public bool CarriesFlag(string name) =>
        Properties.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.True;
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
// model traversals the orchestrator needs. is and has are the built-in generic predicates and are matched
// by name; everything else is reached through the flag an archetype carries, so a model may name its
// archetypes and its wire predicates whatever it likes.
public sealed class PipelineGraph
{
    private const string IsPredicate = "is";

    private const string HasPredicate = "has";

    private readonly Dictionary<Guid, GraphThing> _things;
    private readonly List<GraphRelationship> _relationships;

    public PipelineGraph(IReadOnlyDictionary<Guid, GraphThing> things, IReadOnlyList<GraphRelationship> relationships)
    {
        _things = new Dictionary<Guid, GraphThing>(things);
        _relationships = relationships.ToList();
    }

    public IReadOnlyCollection<GraphThing> Things => _things.Values;

    public GraphThing? Thing(Guid id) => _things.TryGetValue(id, out var t) ? t : null;

    /// <summary>The archetype in this snapshot carrying the given flag, or null when none does. A snapshot
    /// asked for the marked archetypes carries them whether or not the pipeline in it uses one, so a null
    /// here says the model marks that role on nothing (#6516).</summary>
    public GraphThing? ArchetypeCarrying(string roleFlag) =>
        _things.Values.FirstOrDefault(thing => thing.CarriesFlag(roleFlag));

    /// <summary>True when the Thing `is` — directly or transitively — an archetype carrying the given flag.
    /// An archetype does not play its own role, so the walk starts above the Thing.</summary>
    public bool IsOfArchetypeCarrying(GraphThing thing, string roleFlag) =>
        IsOfArchetypeCarrying(thing, roleFlag, new HashSet<Guid> { thing.Id });

    private bool IsOfArchetypeCarrying(GraphThing thing, string roleFlag, HashSet<Guid> seen)
    {
        foreach (var parent in OutgoingTargets(thing, IsPredicate))
        {
            if (!seen.Add(parent.Id)) continue;
            if (parent.CarriesFlag(roleFlag)) return true;
            if (IsOfArchetypeCarrying(parent, roleFlag, seen)) return true;
        }
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

    // Outgoing relationships whose predicate Thing is of an archetype carrying the given flag — how a wire
    // is found without naming either the predicate or the archetype it comes from.
    public IEnumerable<GraphRelationship> OutgoingByPredicateCarrying(GraphThing subject, string roleFlag)
    {
        foreach (var rel in _relationships)
        {
            if (rel.SubjectId != subject.Id) continue;
            if (Thing(rel.PredicateId) is { } predicate && IsOfArchetypeCarrying(predicate, roleFlag))
                yield return rel;
        }
    }

    /// <summary>Things the subject <c>has</c> that are of an archetype carrying <paramref name="roleFlag"/>,
    /// each with the Thing it points at that is of an archetype carrying <paramref name="targetRoleFlag"/> —
    /// how a wire held as a Thing is found, and where it goes, without naming either predicate.
    ///
    /// A wire that points at no such Thing is skipped rather than reported: it is half-drawn, and the shape
    /// this reads is one an editor writes a piece at a time.</summary>
    public IEnumerable<(GraphThing Held, GraphThing Target)> HeldThingsCarrying(
        GraphThing subject, string roleFlag, string targetRoleFlag)
    {
        foreach (var held in OutgoingTargets(subject, HasPredicate))
        {
            if (!IsOfArchetypeCarrying(held, roleFlag)) continue;
            foreach (var rel in _relationships)
            {
                if (rel.SubjectId != held.Id) continue;
                if (Thing(rel.TargetId) is { } target && IsOfArchetypeCarrying(target, targetRoleFlag))
                {
                    yield return (held, target);
                    break;
                }
            }
        }
    }
}
