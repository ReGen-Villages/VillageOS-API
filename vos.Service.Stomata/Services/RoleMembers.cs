using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Stomata.Services;

public static class RoleMembers
{
    public const string IsPredicateName = "is";

    public static IReadOnlyList<SnapshotThing> Of(SnapshotDocument snapshot, Role role)
    {
        if (snapshot.Bound(role) is not [var kind]) return [];
        var isPredicates = snapshot.Things.Where(thing => thing.Name == IsPredicateName).Select(thing => thing.Id).ToHashSet();
        var members = snapshot.Relationships
            .Where(edge => edge.TargetId == kind.Id && isPredicates.Contains(edge.PredicateId))
            .Select(edge => edge.SubjectId)
            .ToHashSet();
        return [.. snapshot.Things.Where(thing => !thing.IsArchetype && members.Contains(thing.Id))];
    }
}
