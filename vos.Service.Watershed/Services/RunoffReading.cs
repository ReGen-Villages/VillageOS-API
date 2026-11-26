using vos.Service.Shared;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;
using vos.Service.Watershed.Terrain;

namespace vos.Service.Watershed.Services;

public sealed record RunoffReading(IReadOnlyList<RunoffClassRule> Classes, IReadOnlyDictionary<long, DesignStormRain> StormsByReturnPeriodYears)
{
    private const string ReturnPeriodYears = "returnPeriodYears";
    private const string AppliesEastOfLongitude = "appliesEastOfLongitude";
    private const int PlacesDeep = 8;

    private static readonly Role[] Roles =
    [
        LandIntakeRoles.RunoffClass, LandIntakeRoles.RunoffClassAlternative, LandIntakeRoles.AlternativeOf, LandIntakeRoles.DesignStorm,
        LandIntakeRoles.RainfallTable, LandIntakeRoles.RainfallIntensity, LandIntakeRoles.ForStorm, LandIntakeRoles.AppliesIn,
        LandIntakeRoles.PlaceNesting,
    ];

    public static SubscriptionSelector Selector(Guid site) => new()
    {
        Ids = [site, .. RoleBindings.Identifiers(Roles)],
        Names = [RoleBindings.BindsPredicateName, GroundReading.Has, PlatformPredicates.Is],
        BoundTypes = RoleBindings.Identifiers(
            LandIntakeRoles.RunoffClass, LandIntakeRoles.RunoffClassAlternative, LandIntakeRoles.DesignStorm,
            LandIntakeRoles.RainfallTable, LandIntakeRoles.RainfallIntensity),
        Traverse =
        [
            new TraverseRule { PredicateRole = LandIntakeRoles.PlaceNesting.Id, Direction = "outgoing", Depth = PlacesDeep },
            RoleBindings.FollowBindings,
        ],
    };

    public static RunoffReading? Read(SnapshotDocument snapshot, Guid site, double longitude)
    {
        var things = snapshot.Things.DistinctBy(thing => thing.Id).ToDictionary(thing => thing.Id);
        Guid? Only(Role role) => snapshot.Bound(role) is [var bound] ? bound.Id : null;
        Guid? Named(string name) => snapshot.Things.FirstOrDefault(thing => thing.Name == name)?.Id;
        IEnumerable<Guid> Along(Guid subject, Guid? predicate) => snapshot.Relationships
            .Where(edge => edge.SubjectId == subject && edge.PredicateId == predicate).Select(edge => edge.TargetId);
        IEnumerable<Guid> Into(Guid target, Guid? predicate) => snapshot.Relationships
            .Where(edge => edge.TargetId == target && edge.PredicateId == predicate).Select(edge => edge.SubjectId);
        SnapshotThing[] MembersOf(Role role) => Only(role) is { } kind
            ? [.. Into(kind, Named(PlatformPredicates.Is)).Select(id => things.GetValueOrDefault(id)).OfType<SnapshotThing>().Where(thing => !thing.IsArchetype)]
            : [];
        IReadOnlyDictionary<string, double> ConditionsOf(SnapshotThing thing) => RunoffClassRules.Known.Keys
            .Where(condition => thing.StatedNumber(condition) is not null)
            .ToDictionary(condition => condition, condition => thing.StatedNumber(condition)!.Value);

        var alternativeOf = Only(LandIntakeRoles.AlternativeOf);
        var alternatives = MembersOf(LandIntakeRoles.RunoffClassAlternative);
        var classes = MembersOf(LandIntakeRoles.RunoffClass)
            .Where(runoffClass => runoffClass.StatedWholeNumber("order") is not null && runoffClass.StatedWholeNumber("gridCode") is not null)
            .Select(runoffClass => new RunoffClassRule(
                runoffClass.StatedWholeNumber("order")!.Value, runoffClass.StatedWholeNumber("gridCode")!.Value, ConditionsOf(runoffClass),
                [.. alternatives.Where(alternative => Along(alternative.Id, alternativeOf).Contains(runoffClass.Id)).Select(ConditionsOf)]))
            .ToList();
        if (classes.Count == 0) return null;

        var table = TableNearest(site, longitude, Only(LandIntakeRoles.PlaceNesting), Only(LandIntakeRoles.AppliesIn),
            MembersOf(LandIntakeRoles.RainfallTable), Along);
        if (table is null) return null;

        var forStorm = Only(LandIntakeRoles.ForStorm);
        var intensities = MembersOf(LandIntakeRoles.RainfallIntensity).ToDictionary(intensity => intensity.Id);
        var held = Along(table.Id, Named(GroundReading.Has)).Select(id => intensities.GetValueOrDefault(id)).OfType<SnapshotThing>().ToList();
        var storms = new Dictionary<long, DesignStormRain>();
        foreach (var storm in MembersOf(LandIntakeRoles.DesignStorm))
        {
            if (storm.StatedWholeNumber(ReturnPeriodYears) is not { } years || storm.StatedNumber("runoffCoefficientMultiplier") is not { } multiplier)
                continue;
            var rain = held
                .Where(intensity => Along(intensity.Id, forStorm).Contains(storm.Id))
                .Where(intensity => intensity.StatedNumber("durationMinutes") is not null && intensity.StatedNumber("millimetresPerHour") is not null)
                .Select(intensity => new RainfallIntensity(intensity.StatedNumber("durationMinutes")!.Value, intensity.StatedNumber("millimetresPerHour")!.Value))
                .ToList();
            if (rain.Count > 0) storms[years] = new DesignStormRain(multiplier, new RainfallIntensities(rain));
        }
        return new RunoffReading(classes, storms);
    }

    private static SnapshotThing? TableNearest(
        Guid site, double longitude, Guid? isIn, Guid? appliesIn, SnapshotThing[] tables, Func<Guid, Guid?, IEnumerable<Guid>> along)
    {
        var seen = new HashSet<Guid> { site };
        var places = along(site, isIn).Where(seen.Add).ToList();
        while (places.Count > 0)
        {
            var applying = tables.Where(table => along(table.Id, appliesIn).Any(places.Contains)).ToList();
            if (applying.Count > 0)
                return applying
                    .Where(table => table.StatedNumber(AppliesEastOfLongitude) is { } east && east < longitude)
                    .MaxBy(table => table.StatedNumber(AppliesEastOfLongitude))
                    ?? applying.Where(table => table.StatedNumber(AppliesEastOfLongitude) is null).MinBy(table => table.Name, StringComparer.Ordinal);
            places = [.. places.SelectMany(place => along(place, isIn)).Where(seen.Add)];
        }
        return null;
    }
}
