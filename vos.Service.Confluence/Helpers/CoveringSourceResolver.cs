using vos.Service.Shared.Subscriptions;

namespace vos.Service.Confluence.Helpers;

// A source that covers the site, and the endpoint registration a call to it goes through.
public sealed record CoveringSource(string Name, string EndpointName);

// One compute service to start on the site's study: the connection to relate through, and the service
// prototype the edge points at. The connection is the predicate, which is what makes the edge dispatch.
public sealed record AnalysisTrigger(string ConnectionName, Guid ConnectionId, Guid ServicePrototypeId);

// The study to compute, and every service that computes part of it.
public sealed record SiteAnalysis(Guid StudyId, IReadOnlyList<AnalysisTrigger> Triggers);

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
    public const string StudiesPredicate = "studies";
    public const string HasPredicate = "has";
    public const string IsPredicateName = "is";

    // The archetype every connection a site analysis dispatches `is`. The connections are found by this
    // mark rather than by name, so adding a balance is an edit to the model: name them here and the two
    // repositories would agree about the analysis only by spelling, which is what #6516 removed.
    public const string SiteAnalysisConnectionFlag = "__IsSiteAnalysisConnectionArchetype";

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
        [IsInPredicate, CoversPredicate, ResolvedByPredicate, StudiesPredicate, HasPredicate, IsPredicateName];

    // Everything in one read: the site's Places, every source whose coverage reaches one of them, the
    // study of the site, and every connection a site analysis dispatches with the service each one binds.
    // Traverse rules compose over the set built so far, so `isIn` must come first — the same ordering
    // trap the endpoint kinds hit, where rules applied before the set is closed find nothing.
    public static SubscriptionSelector SelectorFor(Guid siteId) => new()
    {
        Ids = [siteId],
        Names = [.. PredicatesRead],
        // Model-wide rather than reached from the site: the study is not related to its connections yet,
        // because relating it is what this read is for.
        MarkedTypes = [SiteAnalysisConnectionFlag],
        Traverse =
        [
            new TraverseRule { Predicate = IsInPredicate, Depth = PlaceNestingDepth },
            // Incoming: the edge runs source -> place, and the set so far holds the places.
            new TraverseRule { Predicate = CoversPredicate, Direction = "incoming" },
            new TraverseRule { Predicate = ResolvedByPredicate },
            // Incoming: the edge runs study -> site.
            new TraverseRule { Predicate = StudiesPredicate, Direction = "incoming" },
            // Reaches each connection's service, whose `is` ancestor is the prototype the edge targets.
            new TraverseRule { Predicate = HasPredicate },
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

    // The study of the site and the services that compute it, or null when the site has no study. A site
    // with nothing to compute is not a failed discovery — nothing was ever going to run — so the run
    // reports it and does not treat it as an outage, the same rule a DataSource with no registration is
    // left out under.
    //
    // The study is the subject of every edge written, never the site: a compute service reads its inputs
    // off the study, so an edge naming the site would dispatch the service against a Thing holding none
    // of them.
    public static SiteAnalysis? AnalysisOf(SnapshotDocument snapshot, Guid siteId)
    {
        var thingsById = snapshot.Things.ToDictionary(thing => thing.Id);
        var namesById = thingsById.ToDictionary(entry => entry.Key, entry => entry.Value.Name ?? string.Empty);

        if (StudyOf(snapshot, namesById, siteId) is not { } studyId) return null;

        var triggers = new List<AnalysisTrigger>();
        foreach (var connection in snapshot.Things)
        {
            if (!IsOfArchetypeCarrying(snapshot, thingsById, namesById, connection.Id, SiteAnalysisConnectionFlag))
                continue;
            if (ServicePrototypeOf(snapshot, thingsById, namesById, connection.Id) is not { } prototypeId)
                continue;

            triggers.Add(new AnalysisTrigger(connection.Name ?? string.Empty, connection.Id, prototypeId));
        }

        // Ordered by name so a run writes its edges the same way twice, which is what makes the log of
        // two runs comparable.
        triggers.Sort((left, right) => string.CompareOrdinal(left.ConnectionName, right.ConnectionName));
        return new SiteAnalysis(studyId, triggers);
    }

    private static Guid? StudyOf(
        SnapshotDocument snapshot, IReadOnlyDictionary<Guid, string> namesById, Guid siteId)
    {
        foreach (var edge in snapshot.Relationships)
            if (edge.TargetId == siteId && IsPredicate(namesById, edge.PredicateId, StudiesPredicate))
                return edge.SubjectId;

        return null;
    }

    // A connection binds its service through `has`, and the service `is` the prototype the analysis edge
    // points at — the shape the site-survey template authors by hand.
    private static Guid? ServicePrototypeOf(
        SnapshotDocument snapshot, IReadOnlyDictionary<Guid, SnapshotThing> thingsById,
        IReadOnlyDictionary<Guid, string> namesById, Guid connectionId)
    {
        foreach (var bound in snapshot.Relationships)
        {
            if (bound.SubjectId != connectionId) continue;
            if (!IsPredicate(namesById, bound.PredicateId, HasPredicate)) continue;

            foreach (var typed in snapshot.Relationships)
            {
                if (typed.SubjectId != bound.TargetId) continue;
                if (!IsPredicate(namesById, typed.PredicateId, IsPredicateName)) continue;
                if (thingsById.TryGetValue(typed.TargetId, out var prototype) && prototype.IsArchetype)
                    return typed.TargetId;
            }
        }

        return null;
    }

    // True when the Thing `is` — directly or transitively — an archetype carrying the flag. An archetype
    // does not play its own role, so the walk starts above the Thing.
    private static bool IsOfArchetypeCarrying(
        SnapshotDocument snapshot, IReadOnlyDictionary<Guid, SnapshotThing> thingsById,
        IReadOnlyDictionary<Guid, string> namesById, Guid thingId, string flag)
    {
        var seen = new HashSet<Guid> { thingId };
        var frontier = new Queue<Guid>();
        frontier.Enqueue(thingId);

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            foreach (var edge in snapshot.Relationships)
            {
                if (edge.SubjectId != current) continue;
                if (!IsPredicate(namesById, edge.PredicateId, IsPredicateName)) continue;
                if (!seen.Add(edge.TargetId)) continue;
                if (thingsById.TryGetValue(edge.TargetId, out var ancestor) && CarriesFlag(ancestor, flag))
                    return true;
                frontier.Enqueue(edge.TargetId);
            }
        }

        return false;
    }

    private static bool CarriesFlag(SnapshotThing thing, string flag) =>
        thing.Properties.TryGetValue(flag, out var property)
        && property.Value.ValueKind == System.Text.Json.JsonValueKind.True;

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
