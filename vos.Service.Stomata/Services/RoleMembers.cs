using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Stomata.Services;

public static class RoleMembers
{
    public const string IsPredicateName = "is";

    // Walked down through every kind beneath the bound one, because a member names only its nearest kind.
    public static IReadOnlyList<SnapshotThing> Of(SnapshotDocument snapshot, Role role)
    {
        if (snapshot.Bound(role) is not [var kind]) return [];
        var isPredicates = snapshot.Things.Where(thing => thing.Name == IsPredicateName).Select(thing => thing.Id).ToHashSet();
        var beneath = snapshot.Relationships
            .Where(edge => isPredicates.Contains(edge.PredicateId))
            .ToLookup(edge => edge.TargetId, edge => edge.SubjectId);
        var reached = new HashSet<Guid> { kind.Id };
        var waiting = new Queue<Guid>([kind.Id]);
        while (waiting.TryDequeue(out var above))
            foreach (var below in beneath[above])
                if (reached.Add(below)) waiting.Enqueue(below);
        return [.. snapshot.Things.Where(thing => !thing.IsArchetype && reached.Contains(thing.Id))];
    }
}
