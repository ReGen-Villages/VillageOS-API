using vos.Service.Shared;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;
using vos.Service.Watershed.Terrain;

namespace vos.Service.Watershed.Services;

public sealed record RunoffReading(IReadOnlyList<RunoffClassRule> Classes, IReadOnlyDictionary<long, DesignStormRain> StormsByReturnPeriodYears)
{
    public const string ReturnPeriodYears = "returnPeriodYears";
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

    public static RunoffReading? Read(SnapshotDocument snapshot, Guid site, double longitude) => null;
}
