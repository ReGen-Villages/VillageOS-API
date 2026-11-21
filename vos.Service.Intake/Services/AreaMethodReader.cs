using vos.Service.Intake.Models;
using vos.Service.Shared.Geometry;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Services;

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
        var parcel = DeclaredVocabularyReader.OneBound(snapshot, LandIntakeRoles.Parcel, "what a parcel is");
        var measuresAreaWith = DeclaredVocabularyReader.OneBound(
            snapshot, LandIntakeRoles.MeasuresAreaWith, "how a parcel's area is measured");
        var areaMeasuredWith = DeclaredVocabularyReader.OneBound(
            snapshot, LandIntakeRoles.AreaMeasuredWith, "the method a parcel's area was measured with");

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
            0 => throw new ModelNotSeededError(
                $"the Parcel archetype relates along {measuresAreaWith.Name ?? "measuresAreaWith"} to no method, "
                + "so nothing says how a parcel's area is measured."),
            _ => throw new ModelNotSeededError(
                $"the Parcel archetype relates to more than one method along {measuresAreaWith.Name ?? "measuresAreaWith"} — "
                + string.Join(", ", chosen.Select(id => $"'{NameOf(id)}'"))
                + " — so nothing can say which one a parcel's area is measured with."),
        };

        foreach (var (role, formula) in AreaMethods.Implemented)
            if (snapshot.Bound(role).Any(bound => bound.Id == methodId))
                return new AreaMethodChoice(
                    new DeclaredTerm(NameOf(methodId), methodId), formula, role.Word,
                    new DeclaredTerm(areaMeasuredWith.Name ?? string.Empty, areaMeasuredWith.Id),
                    [
                        .. AreaMethods.Implemented
                            .SelectMany(other => snapshot.Bound(other.Role))
                            .Where(other => other.Id != methodId)
                            .Select(other => new DeclaredTerm(other.Name ?? string.Empty, other.Id))
                            .DistinctBy(other => other.Id),
                    ]);

        throw new ModelNotSeededError(
            $"the Parcel archetype measures area with '{NameOf(methodId)}', which no area method role this service "
            + "implements binds: " + string.Join(", ", AreaMethods.Implemented.Select(implemented => implemented.Role.Word)) + ".");
    }
}
