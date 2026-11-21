using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Shared.Geometry;

public sealed record AreaMethodThing(string Name, Guid Id);

public sealed record AreaMethodChoice(
    AreaMethodThing Method, AreaMethod Formula, string RoleWord, AreaMethodThing AreaMeasuredWith,
    IReadOnlyList<AreaMethodThing> OtherMethods);

public sealed class AreaMethodNotDeclaredException(string message) : Exception(message);

// A model choosing no method, or two, is refused rather than given a default, so no area is written under a method
// the model did not choose.
public static class AreaMethodReader
{
    public static readonly Role[] Roles =
    [
        LandIntakeRoles.Parcel, LandIntakeRoles.MeasuresAreaWith, LandIntakeRoles.AreaMeasuredWith,
        LandIntakeRoles.MeanSphereAreaMethod, LandIntakeRoles.Wgs84EllipsoidAreaMethod,
    ];

    public static AreaMethodChoice Read(SnapshotDocument snapshot)
    {
        var parcel = OneBound(snapshot, LandIntakeRoles.Parcel, "what a parcel is");
        var measuresAreaWith = OneBound(snapshot, LandIntakeRoles.MeasuresAreaWith, "how a parcel's area is measured");
        var areaMeasuredWith = OneBound(snapshot, LandIntakeRoles.AreaMeasuredWith, "the method a parcel's area was measured with");

        var thingsById = snapshot.Things.ToDictionary(thing => thing.Id);
        var chosen = snapshot.Relationships
            .Where(edge => edge.SubjectId == parcel.Id && edge.PredicateId == measuresAreaWith.Id)
            .Select(edge => edge.TargetId)
            .Distinct()
            .ToList();
        string NameOf(Guid id) => thingsById.TryGetValue(id, out var thing) ? thing.Name ?? id.ToString() : id.ToString();

        var methodId = chosen.Count switch
        {
            1 => chosen[0],
            0 => throw new AreaMethodNotDeclaredException(
                $"the Parcel archetype relates along {measuresAreaWith.Name ?? "measuresAreaWith"} to no method, "
                + "so nothing says how a parcel's area is measured."),
            _ => throw new AreaMethodNotDeclaredException(
                $"the Parcel archetype relates to more than one method along {measuresAreaWith.Name ?? "measuresAreaWith"} — "
                + string.Join(", ", chosen.Select(id => $"'{NameOf(id)}'"))
                + " — so nothing can say which one a parcel's area is measured with."),
        };

        foreach (var (role, formula) in AreaMethods.Implemented)
            if (snapshot.Bound(role).Any(bound => bound.Id == methodId))
                return new AreaMethodChoice(
                    new AreaMethodThing(NameOf(methodId), methodId), formula, role.Word,
                    new AreaMethodThing(areaMeasuredWith.Name ?? string.Empty, areaMeasuredWith.Id),
                    [
                        .. AreaMethods.Implemented
                            .SelectMany(other => snapshot.Bound(other.Role))
                            .Where(other => other.Id != methodId)
                            .Select(other => new AreaMethodThing(other.Name ?? string.Empty, other.Id))
                            .DistinctBy(other => other.Id),
                    ]);

        throw new AreaMethodNotDeclaredException(
            $"the Parcel archetype measures area with '{NameOf(methodId)}', which no area method role this service "
            + "implements binds: " + string.Join(", ", AreaMethods.Implemented.Select(implemented => implemented.Role.Word)) + ".");
    }

    private static SnapshotThing OneBound(SnapshotDocument snapshot, Role role, string whatItDeclares)
    {
        var bound = snapshot.Bound(role);
        return bound.Count switch
        {
            1 => bound[0],
            0 => throw new AreaMethodNotDeclaredException(
                $"this model binds nothing to the role {role.Word}, so {whatItDeclares} cannot be read."),
            _ => throw new AreaMethodNotDeclaredException(
                $"this model binds the role {role.Word} to more than one Thing — "
                + string.Join(", ", bound.Select(thing => $"'{thing.Name}'"))
                + $" — so nothing can say which of them {whatItDeclares} is."),
        };
    }
}
