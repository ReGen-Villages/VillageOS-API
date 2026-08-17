using vos.Service.Shared.Subscriptions;

namespace vos.Service.Confluence.Helpers;

// A source that covers the site, and the endpoint registration a call to it goes through.
public sealed record CoveringSource(string Name, string EndpointName);

// Reads which sources cover a site, from one scoped snapshot.
//
// Coverage is edges, never a string: a source `covers` a Place, a site `isIn` a Place, and Places nest
// through `isIn` so a source covering a continent — or the root every Place sits under — covers every
// site within it. Nothing here compares a place name, so a source cannot be silently skipped because a
// country was spelled two ways, which is the failure this whole path exists to prevent.
public static class CoveringSourceResolver
{
    public const string CoversPredicate = "covers";
    public const string IsInPredicate = "isIn";
    public const string ResolvedByPredicate = "resolvedBy";
    public const string AnalysedByPredicate = "analysedBy";

    // Place nesting is a handful of levels — a country inside a region inside the root is the deepest
    // shape anyone has needed. Far beyond that, and costing one unused set expansion per level, which
    // is cheaper than a second round trip to learn the true depth. Same reasoning as the endpoint
    // template chain in Tributary.
    private const int PlaceNestingDepth = 16;

    // The predicate Things are asked for BY NAME, because traversal brings what an edge points at and
    // the incident pass brings the edges, but neither brings the Thing naming one. The walk below
    // compares those names, so a predicate left out here reads as "covers nothing" — a wrong answer
    // wearing the shape of a valid one.
    private static readonly string[] PredicatesRead =
        [IsInPredicate, CoversPredicate, ResolvedByPredicate, AnalysedByPredicate];

    // Everything in one read: the site's Places, then every source whose coverage reaches one of them.
    // Traverse rules compose over the set built so far, so `isIn` must come first — the same ordering
    // trap the endpoint kinds hit, where rules applied before the set is closed find nothing.
    public static SubscriptionSelector SelectorFor(Guid siteId) => new()
    {
        Ids = [siteId],
        Names = [.. PredicatesRead],
        Traverse =
        [
            new TraverseRule { Predicate = IsInPredicate, Depth = PlaceNestingDepth },
            // Incoming: the edge runs source -> place, and the set so far holds the places.
            new TraverseRule { Predicate = CoversPredicate, Direction = "incoming" },
            new TraverseRule { Predicate = ResolvedByPredicate },
            // Runs from the seed, which is in the set throughout, so its position among the rest
            // does not matter — last keeps it out of the ordering the coverage walk depends on.
            new TraverseRule { Predicate = AnalysedByPredicate },
        ],
        IncludeRelationships = true,
    };

    public static IReadOnlyList<CoveringSource> Resolve(SnapshotDocument snapshot, Guid siteId)
    {
        var thingsById = snapshot.Things.ToDictionary(thing => thing.Id);
        var namesById = thingsById.ToDictionary(entry => entry.Key, entry => entry.Value.Name ?? string.Empty);

        var places = PlacesReachedFrom(snapshot, siteId, namesById);

        var covering = new List<CoveringSource>();
        var alreadyTaken = new HashSet<Guid>();

        foreach (var edge in snapshot.Relationships)
        {
            if (!IsPredicate(namesById, edge.PredicateId, CoversPredicate)) continue;
            if (!places.Contains(edge.TargetId)) continue;
            if (!alreadyTaken.Add(edge.SubjectId)) continue;
            if (!thingsById.TryGetValue(edge.SubjectId, out var source)) continue;

            // A source with no registration to call is not a discovery that failed — it is a source
            // that was never callable, and leaving it out here would report it as an outage later.
            if (EndpointOf(snapshot, namesById, thingsById, edge.SubjectId) is not { } endpoint) continue;

            covering.Add(new CoveringSource(source.Name ?? string.Empty, endpoint.Name ?? string.Empty));
        }

        return covering;
    }

    // The pipeline the site is analysed by, or null when it names none. A site that names no pipeline
    // is not a failed discovery — nothing was ever going to run — so the run reports it and does not
    // treat it as an outage, the same rule a DataSource with no registration is left out under.
    public static Guid? AnalysisPipelineOf(SnapshotDocument snapshot, Guid siteId)
    {
        var namesById = snapshot.Things.ToDictionary(thing => thing.Id, thing => thing.Name ?? string.Empty);

        foreach (var edge in snapshot.Relationships)
            if (edge.SubjectId == siteId && IsPredicate(namesById, edge.PredicateId, AnalysedByPredicate))
                return edge.TargetId;

        return null;
    }

    // The site's own Place and every Place containing it. A site relates to one Place; the nesting is
    // what makes a source covering the root cover every site under it without naming any of them.
    private static HashSet<Guid> PlacesReachedFrom(
        SnapshotDocument snapshot, Guid siteId, IReadOnlyDictionary<Guid, string> namesById)
    {
        var reached = new HashSet<Guid>();
        var frontier = new Queue<Guid>();
        frontier.Enqueue(siteId);
        var seen = new HashSet<Guid> { siteId };

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            foreach (var edge in snapshot.Relationships)
            {
                if (edge.SubjectId != current) continue;
                if (!IsPredicate(namesById, edge.PredicateId, IsInPredicate)) continue;
                reached.Add(edge.TargetId);
                if (seen.Add(edge.TargetId))
                    frontier.Enqueue(edge.TargetId);
            }
        }

        return reached;
    }

    private static SnapshotThing? EndpointOf(
        SnapshotDocument snapshot,
        IReadOnlyDictionary<Guid, string> namesById,
        IReadOnlyDictionary<Guid, SnapshotThing> thingsById,
        Guid dataSourceId)
    {
        foreach (var edge in snapshot.Relationships)
            if (edge.SubjectId == dataSourceId
                && IsPredicate(namesById, edge.PredicateId, ResolvedByPredicate)
                && thingsById.TryGetValue(edge.TargetId, out var endpoint))
                return endpoint;

        return null;
    }

    private static bool IsPredicate(
        IReadOnlyDictionary<Guid, string> namesById, Guid predicateId, string name) =>
        namesById.TryGetValue(predicateId, out var predicate)
        && string.Equals(predicate, name, StringComparison.OrdinalIgnoreCase);
}
