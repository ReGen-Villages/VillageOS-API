using vos.Service.Shared.Subscriptions;

namespace vos.Service.Tributary.Helpers;

// The `observed` edges an endpoint already carries, read from the same scoped snapshot its kinds come
// from. The ingest relates the endpoint to every Thing it writes onto, so a later run over the same
// Thing has to know the edge is already there. Reading it here costs no call of its own: every edge
// incident to the endpoint is in that snapshot whether or not anything looks at it.
public sealed record ObservedEdges(Guid? PredicateId, IReadOnlySet<Guid> ObservedThingIds)
{
    public const string PredicateName = "observed";

    // A model holding no `observed` Thing at all: nothing to compare against and no predicate to reuse,
    // so the ingest resolves or creates one itself.
    public static readonly ObservedEdges None = new(null, new HashSet<Guid>());

    // The predicate is taken from an edge already in use before it is taken from the Thing named
    // `observed`, because a name matches more than one Thing and only the one the endpoint already
    // relates through keeps a second run from writing a parallel edge beside the first.
    public static ObservedEdges Resolve(SnapshotDocument snapshot, Guid endpointId)
    {
        var named = snapshot.Things
            .Where(thing => string.Equals(thing.Name, PredicateName, StringComparison.OrdinalIgnoreCase))
            .Select(thing => thing.Id)
            .ToHashSet();
        if (named.Count == 0) return None;

        Guid? predicateId = null;
        var observed = new HashSet<Guid>();
        foreach (var edge in snapshot.Relationships)
        {
            if (edge.SubjectId != endpointId) continue;
            if (!named.Contains(edge.PredicateId)) continue;
            predicateId ??= edge.PredicateId;
            observed.Add(edge.TargetId);
        }

        // With no edge to read it off, one Thing of that name is the predicate to write with. Two are
        // left to the ingest rather than picked between, so nothing relates through whichever Thing the
        // snapshot happened to list first.
        if (predicateId is null && named.Count == 1)
            predicateId = named.First();

        return new ObservedEdges(predicateId, observed);
    }
}
