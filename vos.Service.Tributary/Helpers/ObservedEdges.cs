using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Tributary.Helpers;

// The `observed` relationships an endpoint already carries, read from the same scoped snapshot its kinds come
// from. The ingest relates the endpoint to every Thing it writes onto, so a later run over the same
// Thing has to know the relationship is already there. Reading it here costs no call of its own: every relationship
// incident to the endpoint is in that snapshot whether or not anything looks at it.
public sealed record ObservedEdges(Guid? PredicateId, IReadOnlySet<Guid> ObservedThingIds, Guid? PredicateRootId)
{
    public const string PredicateName = "observed";

    public static readonly ObservedEdges None = new(null, new HashSet<Guid>(), null);

    // The platform's open-data-sources template declares `observed` under this identifier, so a model that imports
    // the template after Tributary created the predicate still holds one.
    private static readonly Guid UrlNamespace = new("6ba7b811-9dad-11d1-80b4-00c04fd430c8");
    private const string OpenDataSourcesTemplatePrefix = "villageos:open-data-sources:";

    public static ObservedEdges Resolve(SnapshotDocument snapshot, Guid endpointId)
    {
        var roots = snapshot.Bound(LandIntakeRoles.Predicate);
        Guid? predicateRootId = roots.Count == 1 ? roots[0].Id : null;

        var bound = snapshot.Bound(LandIntakeRoles.Observed);
        if (bound.Count != 1) return None with { PredicateRootId = predicateRootId };

        var predicateId = bound[0].Id;
        var observed = snapshot.Relationships
            .Where(edge => edge.SubjectId == endpointId && edge.PredicateId == predicateId)
            .Select(edge => edge.TargetId)
            .ToHashSet();
        return new ObservedEdges(predicateId, observed, predicateRootId);
    }

    public static Guid CreatedPredicateId { get; } =
        StableIdentifier.DeriveVersion5(UrlNamespace, OpenDataSourcesTemplatePrefix + PredicateName);

    private static Guid IdentifierOfAPredicateTheModelLacks(string predicateName) =>
        StableIdentifier.DeriveVersion5(RoleIdentity.Namespace, "predicate:" + predicateName);

    public static object CreationFragment(Guid? heldIsPredicate, Guid? heldBindsPredicate, Guid? predicateRoot)
    {
        var isPredicate = heldIsPredicate ?? IdentifierOfAPredicateTheModelLacks("is");
        var bindsPredicate = heldBindsPredicate ?? IdentifierOfAPredicateTheModelLacks(RoleBindings.BindsPredicateName);
        var things = new List<object>
        {
            new { Id = CreatedPredicateId, Name = PredicateName },
            new { Id = LandIntakeRoles.Observed.Id, Name = "Observed" },
            new { Id = RoleIdentity.Of("role"), Name = "Role", IsArchetype = true },
        };
        if (heldIsPredicate is null) things.Add(new { Id = isPredicate, Name = "is" });
        if (heldBindsPredicate is null) things.Add(new { Id = bindsPredicate, Name = RoleBindings.BindsPredicateName });

        var relationships = new List<object>
        {
            new { Subject = LandIntakeRoles.Observed.Id, Predicate = bindsPredicate, Target = CreatedPredicateId },
            new { Subject = LandIntakeRoles.Observed.Id, Predicate = isPredicate, Target = RoleIdentity.Of("role") },
        };
        if (predicateRoot is { } root)
            relationships.Add(new { Subject = CreatedPredicateId, Predicate = isPredicate, Target = root });

        return new { Name = PredicateName, Things = things, Relationships = relationships };
    }
}
