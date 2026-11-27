using System.Text.Json;
using vos.Service.Shared;
using vos.Service.Shared.DagNode;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;
using vos.Service.Watershed.Terrain;

namespace vos.Service.Watershed.Services;

public sealed record GroundReading(
    SnapshotThing Study, SnapshotThing? Site, IReadOnlyList<Position>? Border, string? HeightsSource,
    IReadOnlyList<SlopeClassBand> SlopeClasses, IReadOnlyList<AspectSector> Aspects, IReadOnlyDictionary<Guid, Guid> ShareAssessing)
{
    public const string BoundaryProperty = "boundary";
    // Read by name until the holding role replaces it (VillageOS 8417, bound by 8771's template).
    public const string Has = "has";

    private static readonly Role[] Roles =
    [
        LandIntakeRoles.StudiedSite, LandIntakeRoles.Assesses, LandIntakeRoles.Parcel, LandIntakeRoles.SlopeClassShare,
        LandIntakeRoles.AspectShare, LandIntakeRoles.BorderElevation,
    ];

    // The bindings are followed last, so the walk along `has` does not spread from the kinds the roles bind.
    public static SubscriptionSelector Selector(Guid study) => new()
    {
        Ids = [study, .. RoleBindings.Identifiers(Roles)],
        Names = [RoleBindings.BindsPredicateName, Has, PlatformPredicates.Is],
        Traverse =
        [
            new TraverseRule { PredicateRole = LandIntakeRoles.StudiedSite.Id, Direction = "outgoing", Depth = 1 },
            new TraverseRule { Predicate = Has, Direction = "outgoing", Depth = 1 },
            new TraverseRule { PredicateRole = LandIntakeRoles.Assesses.Id, Direction = "outgoing", Depth = 1 },
            RoleBindings.FollowBindings,
        ],
    };

    public static GroundReading Read(SnapshotDocument snapshot, Guid studyId)
    {
        var things = snapshot.Things.DistinctBy(thing => thing.Id).ToDictionary(thing => thing.Id);
        if (!things.TryGetValue(studyId, out var study))
            throw new NodeFailedException($"The model holds no SiteStudy {studyId}, so there is no ground to work out.");

        Guid? Only(Role role) => snapshot.Bound(role) is [var bound] ? bound.Id : null;
        Guid? Named(string name) => snapshot.Things.FirstOrDefault(thing => thing.Name == name)?.Id;
        IEnumerable<Guid> Along(Guid subject, Guid? predicate) => snapshot.Relationships
            .Where(edge => edge.SubjectId == subject && edge.PredicateId == predicate).Select(edge => edge.TargetId);
        bool IsA(Guid thing, Guid? kind) => kind is not null && Along(thing, Named(PlatformPredicates.Is)).Contains(kind.Value);

        var site = Along(studyId, Only(LandIntakeRoles.StudiedSite)).Select(id => things.GetValueOrDefault(id)).FirstOrDefault(thing => thing is not null);
        if (site is null) return new GroundReading(study, null, null, null, [], [], new Dictionary<Guid, Guid>());

        var held = Along(site.Id, Named(Has)).ToArray();
        var parcel = held.FirstOrDefault(thing => IsA(thing, Only(LandIntakeRoles.Parcel)));
        var border = things.GetValueOrDefault(parcel) is { } parcelThing ? BorderOf(parcelThing) : null;
        var source = snapshot.Bound(LandIntakeRoles.BorderElevation).OrderBy(thing => thing.Name, StringComparer.Ordinal).FirstOrDefault()?.Name;

        var shareAssessing = new Dictionary<Guid, Guid>();
        var slopeClasses = new List<SlopeClassBand>();
        var aspects = new List<AspectSector>();
        foreach (var share in held)
        {
            if (Along(share, Only(LandIntakeRoles.Assesses)).Select(id => things.GetValueOrDefault(id)).FirstOrDefault(term => term is not null) is not { } term)
                continue;
            if (IsA(share, Only(LandIntakeRoles.SlopeClassShare)))
            {
                slopeClasses.Add(new SlopeClassBand(term.Id, term.StatedNumber("atLeastPercent"), term.StatedNumber("belowPercent")));
                shareAssessing[term.Id] = share;
            }
            else if (IsA(share, Only(LandIntakeRoles.AspectShare)))
            {
                aspects.Add(new AspectSector(term.Id, term.StatedNumber("centreDegrees")));
                shareAssessing[term.Id] = share;
            }
        }

        return new GroundReading(study, site, border, source, slopeClasses, aspects, shareAssessing);
    }

    private static IReadOnlyList<Position>? BorderOf(SnapshotThing parcel)
    {
        if (parcel.StatedValue(BoundaryProperty) is not { } stated) return null;
        try
        {
            var polygon = stated.Value.ValueKind == JsonValueKind.String ? JsonDocument.Parse(stated.Value.GetString()!).RootElement : stated.Value;
            if (!polygon.TryGetProperty("type", out var type) || type.GetString() != "Polygon"
                || !polygon.TryGetProperty("coordinates", out var rings) || rings.ValueKind != JsonValueKind.Array || rings.GetArrayLength() == 0)
                return null;
            var corners = rings[0].EnumerateArray().Select(position => new Position(position[1].GetDouble(), position[0].GetDouble())).ToList();
            if (corners.Count > 1 && corners[0] == corners[^1]) corners.RemoveAt(corners.Count - 1);
            return corners.Count >= 3 ? corners : null;
        }
        catch (Exception unreadable) when (unreadable is JsonException or InvalidOperationException or IndexOutOfRangeException)
        {
            return null;
        }
    }
}
