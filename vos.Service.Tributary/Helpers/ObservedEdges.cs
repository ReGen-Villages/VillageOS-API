using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Tributary.Helpers;

// The `observed` relationships an endpoint already carries, read from the same scoped snapshot its kinds come
// from. The ingest relates the endpoint to every Thing it writes onto, so a later run over the same
// Thing has to know the relationship is already there. Reading it here costs no call of its own: every relationship
// incident to the endpoint is in that snapshot whether or not anything looks at it.
public sealed record ObservedEdges(Guid? PredicateId, IReadOnlySet<Guid> ObservedThingIds)
{
    public const string PredicateName = "observed";

    public static readonly ObservedEdges None = new(null, new HashSet<Guid>());

    public static ObservedEdges Resolve(SnapshotDocument snapshot, Guid endpointId)
    {
        var bound = snapshot.Bound(LandIntakeRoles.Observed);
        if (bound.Count != 1) return None;

        var predicateId = bound[0].Id;
        var observed = snapshot.Relationships
            .Where(edge => edge.SubjectId == endpointId && edge.PredicateId == predicateId)
            .Select(edge => edge.TargetId)
            .ToHashSet();
        return new ObservedEdges(predicateId, observed);
    }

    public static Guid MintedPredicateId => Derived(PredicateName);

    public static Guid Derived(string predicateName) =>
        StableIdentifier.DeriveVersion5(RoleIdentity.Namespace, "predicate:" + predicateName);

    public static object MintingFragment(Guid? heldIsPredicate, Guid? heldBindsPredicate)
    {
        var isPredicate = heldIsPredicate ?? Derived("is");
        var bindsPredicate = heldBindsPredicate ?? Derived(RoleBindings.BindsPredicateName);
        var things = new List<object>
        {
            new { Id = MintedPredicateId, Name = PredicateName },
            new { Id = LandIntakeRoles.Observed.Id, Name = "Observed" },
            new { Id = RoleIdentity.Of("role"), Name = "Role", IsArchetype = true },
        };
        if (heldIsPredicate is null) things.Add(new { Id = isPredicate, Name = "is" });
        if (heldBindsPredicate is null) things.Add(new { Id = bindsPredicate, Name = RoleBindings.BindsPredicateName });

        return new
        {
            Name = PredicateName,
            Things = things,
            Relationships = new object[]
            {
                new { Subject = LandIntakeRoles.Observed.Id, Predicate = bindsPredicate, Target = MintedPredicateId },
                new { Subject = LandIntakeRoles.Observed.Id, Predicate = isPredicate, Target = RoleIdentity.Of("role") },
            },
        };
    }
}
