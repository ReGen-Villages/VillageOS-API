using vos.Service.Shared.Roles;

namespace vos.Service.Shared.Subscriptions;

// Reading a role's binding off a snapshot. A role Thing reaches the one Thing playing it along `binds`, one of
// the platform's own predicates and so found by name. The snapshot has to carry the role Thing, the `binds`
// predicate Thing and the relationship between the two: a selector asks for the role Things by identifier,
// names `binds`, and follows FollowBindings.
public static class RoleBindings
{
    public const string BindsPredicateName = "binds";

    public static readonly TraverseRule FollowBindings = new() { Predicate = BindsPredicateName, Direction = "outgoing", Depth = 1 };

    public static List<Guid> Identifiers(params IEnumerable<Role> roles) => [.. roles.Select(role => role.Id)];

    // Every Thing the role binds in this snapshot: one in a model that declares the role, none where it does not,
    // and more than one only in a model seed validation would have refused. The caller says what each case means
    // for the question it was asking.
    public static IReadOnlyList<SnapshotThing> Bound(this SnapshotDocument snapshot, Role role)
    {
        var binds = snapshot.Things
            .Where(thing => string.Equals(thing.Name, BindsPredicateName, StringComparison.Ordinal))
            .Select(thing => thing.Id)
            .ToHashSet();
        if (binds.Count == 0) return [];

        var thingsById = snapshot.Things.ToDictionary(thing => thing.Id);
        return [.. snapshot.Relationships
            .Where(edge => edge.SubjectId == role.Id && binds.Contains(edge.PredicateId))
            .Select(edge => thingsById.GetValueOrDefault(edge.TargetId))
            .OfType<SnapshotThing>()];
    }
}
