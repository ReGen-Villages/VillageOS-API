using vos.Service.Shared;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Tributary.Helpers;

// A kind an endpoint reached, and what that kind declares an endpoint using it must supply.
public sealed record ResolvedKind(string Name, IReadOnlyList<string> Requires);

// Reads which kinds an endpoint reaches, from one scoped snapshot.
//
// The edges live on the template, not on the registration that `is` it, and the selector applies its
// traverse rules BEFORE it closes over `is` ancestors — so asking for the role predicates alone finds
// nothing. Traverse rules compose over the set built so far, so walking `is` first and the roles
// after reaches the kinds in a single call.
public static class EndpointKindResolver
{
    // An endpoint catalogue is a shallow hierarchy — a root, a source type, and a registration is the
    // shape Delta provisions. This is far deeper than any real chain and costs one set expansion per
    // unused level, which is cheaper than a second round trip to discover the chain's true length.
    private const int TemplateChainDepth = 16;

    // The predicate Things are asked for BY NAME. Nothing else brings them: traversal adds the Things
    // an edge points at, and the incident pass adds the edges, but the Thing naming an edge is neither.
    // The walk below compares that name, so a predicate left out here is a role that matches nothing
    // and reads as "reaches no kind" — a wrong answer that looks like a valid one.
    private static readonly string[] PredicatesRead = ["is", .. EndpointKindRoles.All];

    public static SubscriptionSelector SelectorFor(Guid endpointId) => new()
    {
        Ids = [endpointId],
        Names = [.. PredicatesRead],
        Traverse =
        [
            new TraverseRule { Predicate = "is", Depth = TemplateChainDepth },
            .. EndpointKindRoles.All.Select(role => new TraverseRule { Predicate = role }),
        ],
        IncludeRelationships = true,
    };

    // The kind the endpoint reaches for each role, nearest first. A role the endpoint reaches no kind
    // for is absent from the result rather than present and empty — reaching nothing and reaching a
    // kind that requires nothing are different, and only the second is a decision the model made.
    public static IReadOnlyDictionary<string, ResolvedKind> Resolve(SnapshotDocument snapshot, Guid endpointId)
    {
        var resolved = new Dictionary<string, ResolvedKind>(StringComparer.OrdinalIgnoreCase);
        var thingsById = snapshot.Things.ToDictionary(thing => thing.Id);
        var namesById = thingsById.ToDictionary(entry => entry.Key, entry => entry.Value.Name ?? string.Empty);

        foreach (var subjectId in ChainFrom(snapshot, endpointId, thingsById))
        {
            foreach (var edge in snapshot.Relationships)
            {
                if (edge.SubjectId != subjectId) continue;
                if (!namesById.TryGetValue(edge.PredicateId, out var role)) continue;
                if (!EndpointKindRoles.All.Contains(role, StringComparer.OrdinalIgnoreCase)) continue;
                if (resolved.ContainsKey(role)) continue;
                if (!thingsById.TryGetValue(edge.TargetId, out var kind)) continue;

                resolved[role] = new ResolvedKind(kind.Name ?? string.Empty, [.. kind.Properties.Keys]);
            }
        }

        return resolved;
    }

    // The endpoint and then its templates, nearest first, so the closest declaration of a role wins —
    // the same closest-ancestor-wins rule a narrowed property follows.
    private static IEnumerable<Guid> ChainFrom(
        SnapshotDocument snapshot, Guid start, IReadOnlyDictionary<Guid, SnapshotThing> thingsById)
    {
        var seen = new HashSet<Guid>();
        var frontier = new Queue<Guid>();
        frontier.Enqueue(start);

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            if (!seen.Add(current)) continue;
            yield return current;

            foreach (var edge in snapshot.Relationships)
                if (edge.SubjectId == current
                    && thingsById.TryGetValue(edge.PredicateId, out var predicate)
                    && string.Equals(predicate.Name, "is", StringComparison.OrdinalIgnoreCase))
                    frontier.Enqueue(edge.TargetId);
        }
    }

    // What the endpoint's kind for this role requires and the endpoint does not supply. Empty when the
    // kind is satisfied, and when there is no kind to satisfy.
    // Takes the map the caller already holds. Widening this to a read-only view would mean copying it
    // once per requirement checked, on the path that serves an outbound call — see Bug 6486, which
    // makes the shared lookup read-only and lets this widen for free.
    public static IReadOnlyList<string> MissingRequirements(
        ResolvedKind? kind, Dictionary<string, System.Text.Json.JsonElement> effective) =>
        kind == null
            ? Array.Empty<string>()
            : kind.Requires.Where(required => !Supplies(effective, required)).ToList();

    // A blank value is not a supplied one. A structural key declared and left empty is how a template
    // says "whoever registers this fills it in", so treating it as present would let exactly the
    // endpoint this check exists to catch through.
    private static bool Supplies(
        Dictionary<string, System.Text.Json.JsonElement> effective, string required) =>
        EffectivePropertyResolver.TryGetEffectiveProperty(effective, required, out var value, out _)
        && value.ValueKind != System.Text.Json.JsonValueKind.Null
        && !(value.ValueKind == System.Text.Json.JsonValueKind.String
             && string.IsNullOrWhiteSpace(value.GetString()));
}
