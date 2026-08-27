using System.Text.Json;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Forage.Helpers;

// One fetch a covering source is called through: the Thing the reading is about, and the values the
// registration's address may name. A source is usually called once with the site as the subject; a
// source that resolves onto an archetype is called once per Thing the site has of it.
public sealed record SourceCall(
    Guid SubjectId, string SubjectName, IReadOnlyDictionary<string, string> Values);

// A source that covers the site, the endpoint registration a call to it goes through, and the calls
// a run makes to it. A source with no calls declared what it resolves onto and the site holds
// nothing of it — nothing to fetch, and not a failure.
public sealed record CoveringSource(string Name, string EndpointName, IReadOnlyList<SourceCall> Calls);

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
    public const string ResolvesOntoPredicate = "resolvesOnto";
    public const string AssessesPredicate = "assesses";
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
    [
        IsInPredicate, CoversPredicate, ResolvedByPredicate, ResolvesOntoPredicate,
        AssessesPredicate, StudiesPredicate, HasPredicate, IsPredicateName,
    ];

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
            // After covers: the sources are in the set, and what each declares it resolves onto joins it.
            new TraverseRule { Predicate = ResolvesOntoPredicate },
            // Incoming: the edge runs study -> site.
            new TraverseRule { Predicate = StudiesPredicate, Direction = "incoming" },
            // Reaches each connection's service, and everything the site has — a per-subject call below
            // is addressed with what its subject reaches, so the subjects must be in the set first.
            new TraverseRule { Predicate = HasPredicate },
            // After has: what each of the site's assessments is about carries a portal's code for it.
            new TraverseRule { Predicate = AssessesPredicate },
        ],
        IncludeRelationships = true,
    };

    public static IReadOnlyList<CoveringSource> Resolve(SnapshotDocument snapshot, Guid siteId)
    {
        var thingsById = snapshot.Things.ToDictionary(thing => thing.Id);
        var namesById = thingsById.ToDictionary(entry => entry.Key, entry => entry.Value.Name ?? string.Empty);

        var placesByDepth = PlacesByDepth(snapshot, siteId, namesById);
        var places = placesByDepth.SelectMany(level => level).ToHashSet();

        // The site's own values first, then each Place's, nearest first: a value the site carries is
        // about the site, and a division's value is about somewhere smaller than its country's.
        var siteValues = Layered(
            [OwnValues.Of(snapshot, siteId), .. placesByDepth.Select(level => AgreedValues(snapshot, level))]);

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

            var calls = CallsFor(snapshot, thingsById, namesById, edge.SubjectId, siteId, siteValues);
            covering.Add(new CoveringSource(
                source.Name ?? string.Empty, endpoint.Name ?? string.Empty, calls));
        }

        return covering;
    }

    // The calls one covering source is fetched through. A source that declares nothing it resolves
    // onto is called once, about the site. One that does is called once per Thing the site has of the
    // declared archetype, about that Thing — addressed with the subject's own values, then the values
    // of what it reaches, then the site's, so the most specific holder of a name decides it.
    private static IReadOnlyList<SourceCall> CallsFor(
        SnapshotDocument snapshot,
        IReadOnlyDictionary<Guid, SnapshotThing> thingsById,
        IReadOnlyDictionary<Guid, string> namesById,
        Guid sourceId,
        Guid siteId,
        IReadOnlyDictionary<string, string> siteValues)
    {
        var declared = snapshot.Relationships
            .Where(edge => edge.SubjectId == sourceId
                && IsPredicate(namesById, edge.PredicateId, ResolvesOntoPredicate))
            .Select(edge => edge.TargetId)
            .ToList();

        if (declared.Count == 0)
            return [new SourceCall(siteId, namesById.GetValueOrDefault(siteId, string.Empty), siteValues)];

        // A declaration pointing at anything but an archetype resolves onto nothing. Falling back to
        // a per-site call would be refused for its unfilled placeholders, and the run would report
        // the provider for an outage it had no part in.
        var resolutionArchetypes = declared
            .Where(targetId => thingsById.TryGetValue(targetId, out var target) && target.IsArchetype)
            .ToList();

        var owned = snapshot.Relationships
            .Where(edge => edge.SubjectId == siteId && IsPredicate(namesById, edge.PredicateId, HasPredicate))
            .Select(edge => edge.TargetId)
            .ToHashSet();

        var subjects = MembersReachedFrom(snapshot, thingsById, namesById, resolutionArchetypes)
            .Where(owned.Contains)
            .OrderBy(subjectId => namesById.GetValueOrDefault(subjectId, string.Empty), StringComparer.Ordinal)
            .ThenBy(subjectId => subjectId)
            .ToList();

        return subjects
            .Select(subjectId => new SourceCall(
                subjectId,
                namesById.GetValueOrDefault(subjectId, string.Empty),
                Layered(
                [
                    OwnValues.Of(snapshot, subjectId),
                    AgreedValues(snapshot, VocabularyOf(snapshot, namesById, subjectId)),
                    siteValues,
                ])))
            .ToList();
    }

    // The Things a subject reaches by its own outgoing edges — for an assessment, the type it assesses
    // and the source it hangs off. `is` is not among them: a type's values are defaults for a kind, the
    // same reason inherited values never address a call.
    private static List<Guid> VocabularyOf(
        SnapshotDocument snapshot, IReadOnlyDictionary<Guid, string> namesById, Guid subjectId) =>
        snapshot.Relationships
            .Where(edge => edge.SubjectId == subjectId
                && !IsPredicate(namesById, edge.PredicateId, IsPredicateName))
            .Select(edge => edge.TargetId)
            .ToList();

    // One set of values from several Things standing at the same distance. A name they agree on is a
    // value; one they disagree on is dropped, because relationship order is not defined and taking
    // either would address different calls on different runs.
    private static Dictionary<string, string> AgreedValues(SnapshotDocument snapshot, IEnumerable<Guid> ids)
    {
        var agreed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var contested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            foreach (var (name, value) in OwnValues.Of(snapshot, id))
            {
                if (contested.Contains(name)) continue;
                if (!agreed.TryGetValue(name, out var existing))
                {
                    agreed[name] = value;
                }
                else if (!string.Equals(existing, value, StringComparison.Ordinal))
                {
                    agreed.Remove(name);
                    contested.Add(name);
                }
            }
        }

        return agreed;
    }

    // Most specific first: a layer fills only the names no earlier layer decided.
    private static Dictionary<string, string> Layered(
        IReadOnlyList<IReadOnlyDictionary<string, string>> layers)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in layers)
            foreach (var (name, value) in layer)
                if (!values.ContainsKey(name))
                    values[name] = value;

        return values;
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
        foreach (var connectionId in MembersOfArchetypesCarrying(
                     snapshot, thingsById, namesById, SiteAnalysisConnectionFlag))
        {
            if (ServicePrototypeOf(snapshot, thingsById, namesById, connectionId) is not { } prototypeId)
                continue;

            triggers.Add(new AnalysisTrigger(
                thingsById[connectionId].Name ?? string.Empty, connectionId, prototypeId));
        }

        // Ordered by name so a run writes its edges the same way twice, which is what makes the log of
        // two runs comparable.
        triggers.Sort((left, right) => string.CompareOrdinal(left.ConnectionName, right.ConnectionName));
        return new SiteAnalysis(studyId, triggers);
    }

    // The one study of the site. More than one is refused rather than picked between: relationship order
    // is not defined, so choosing would analyse a different study on different runs and report neither
    // choice. Null means none, which the caller reports as nothing to compute.
    private static Guid? StudyOf(
        SnapshotDocument snapshot, IReadOnlyDictionary<Guid, string> namesById, Guid siteId)
    {
        Guid? found = null;
        foreach (var edge in snapshot.Relationships)
        {
            if (edge.TargetId != siteId) continue;
            if (!IsPredicate(namesById, edge.PredicateId, StudiesPredicate)) continue;
            if (found != null && found != edge.SubjectId) return null;
            found = edge.SubjectId;
        }

        return found;
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

    private static List<Guid> MembersOfArchetypesCarrying(
        SnapshotDocument snapshot, IReadOnlyDictionary<Guid, SnapshotThing> thingsById,
        IReadOnlyDictionary<Guid, string> namesById, string flag) =>
        MembersReachedFrom(
            snapshot, thingsById, namesById,
            snapshot.Things.Where(thing => CarriesFlag(thing, flag)).Select(thing => thing.Id));

    // Every Thing that `is` — directly or through intermediate types — one of the given archetypes.
    // Walked outwards from them rather than upwards from each Thing, so the snapshot's relationships
    // are read once per level instead of once per Thing.
    //
    // An archetype does not play its own role, so an archetype is not among its own members, and an
    // intermediate type between a member and the top is a type rather than a member.
    private static List<Guid> MembersReachedFrom(
        SnapshotDocument snapshot, IReadOnlyDictionary<Guid, SnapshotThing> thingsById,
        IReadOnlyDictionary<Guid, string> namesById, IEnumerable<Guid> archetypeIds)
    {
        var reached = new HashSet<Guid>(archetypeIds);
        var members = new List<Guid>();
        var frontier = new Queue<Guid>(reached);

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            foreach (var edge in snapshot.Relationships)
            {
                if (edge.TargetId != current) continue;
                if (!IsPredicate(namesById, edge.PredicateId, IsPredicateName)) continue;
                if (!reached.Add(edge.SubjectId)) continue;

                frontier.Enqueue(edge.SubjectId);
                if (thingsById.TryGetValue(edge.SubjectId, out var member) && !member.IsArchetype)
                    members.Add(edge.SubjectId);
            }
        }

        return members;
    }

    private static bool CarriesFlag(SnapshotThing thing, string flag) =>
        thing.Properties.TryGetValue(flag, out var property)
        && property.Value.ValueKind == JsonValueKind.True;

    // The site's own Place and every Place containing it, grouped by how many isIn edges away each
    // stands. The nesting is what makes a source covering the root cover every site under it without
    // naming any of them; the grouping is what lets the nearest Place's value win an address name.
    private static List<List<Guid>> PlacesByDepth(
        SnapshotDocument snapshot, Guid siteId, IReadOnlyDictionary<Guid, string> namesById)
    {
        var levels = new List<List<Guid>>();
        var seen = new HashSet<Guid> { siteId };
        var frontier = new List<Guid> { siteId };

        while (frontier.Count > 0)
        {
            var next = new List<Guid>();
            foreach (var current in frontier)
                foreach (var edge in snapshot.Relationships)
                {
                    if (edge.SubjectId != current) continue;
                    if (!IsPredicate(namesById, edge.PredicateId, IsInPredicate)) continue;
                    if (seen.Add(edge.TargetId))
                        next.Add(edge.TargetId);
                }

            if (next.Count > 0) levels.Add(next);
            frontier = next;
        }

        return levels;
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
