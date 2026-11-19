using vos.Service.Shared;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Forage.Helpers;

internal static class SnapshotWalk
{
    public static bool IsPredicate(IReadOnlyDictionary<Guid, string> namesById, Guid predicateId, string name) =>
        namesById.TryGetValue(predicateId, out var predicate)
        && string.Equals(predicate, name, StringComparison.Ordinal);

    // Walked outwards from the roots rather than upwards from each Thing, so the snapshot's relationships
    // are read once per level instead of once per Thing.
    public static List<Guid> ThingsUnder(
        SnapshotDocument snapshot, IReadOnlyDictionary<Guid, string> namesById, IEnumerable<Guid> roots)
    {
        var reached = new HashSet<Guid>(roots);
        var under = new List<Guid>();
        var frontier = new Queue<Guid>(reached);

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            foreach (var edge in snapshot.Relationships)
            {
                if (edge.TargetId != current) continue;
                if (!IsPredicate(namesById, edge.PredicateId, PlatformPredicates.Is)) continue;
                if (!reached.Add(edge.SubjectId)) continue;

                frontier.Enqueue(edge.SubjectId);
                under.Add(edge.SubjectId);
            }
        }

        return under;
    }
}
