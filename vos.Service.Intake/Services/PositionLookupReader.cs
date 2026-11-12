using System.Text.Json;
using vos.Service.Intake.Models;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Services;

// The lookups a page makes before any site exists — the legal parcel at a clicked position, and place
// names for what somebody typed — read from the model that registers them.
//
// Each registration is found through the role that binds it, as every vocabulary this service reads is. What is
// read off it is what this service needs and nothing Tributary already resolves for itself: the name a
// call is forwarded under, the reshape this service applies to the raw body — under a property of its
// own, because Tributary ingests a responseTransform and a position that is not yet a site has
// nothing to ingest onto — whose record the answer is, and for a parcel register the bounds it covers.
// Bounds are a pair of spans rather than a covers relationship: a clicked position belongs to no site,
// so the coverage walk that selects discovery sources has nowhere to start.
public static class PositionLookupReader
{
    public static readonly Role ParcelLookupRole = LandIntakeRoles.ParcelLookup;
    public static readonly Role PlaceSearchRole = LandIntakeRoles.PlaceSearch;
    public static readonly Role FallsBackToRole = LandIntakeRoles.FallsBackTo;
    public static readonly Role BorderElevationRole = LandIntakeRoles.BorderElevation;
    public static readonly Role[] Roles = [ParcelLookupRole, PlaceSearchRole, FallsBackToRole, BorderElevationRole];

    // Deeper than any order a deployment declares; a chain that loops back on itself ends at the first repeat.
    public static readonly TraverseRule FollowFallbacks =
        new() { PredicateRole = FallsBackToRole.Id, Direction = "outgoing", Depth = 8 };

    public const string TransformProperty = "lookupTransform";
    public const string AttributionProperty = "attribution";
    public const string SouthLatitudeProperty = "boundsSouthLatitude";
    public const string NorthLatitudeProperty = "boundsNorthLatitude";
    public const string WestLongitudeProperty = "boundsWestLongitude";
    public const string EastLongitudeProperty = "boundsEastLongitude";

    public static SubscriptionSelector Selector() => new()
    {
        Ids = RoleBindings.Identifiers(Roles),
        Names = [RoleBindings.BindsPredicateName],
        BoundArchetypes = RoleBindings.Identifiers(Roles),
        Traverse = [RoleBindings.FollowBindings, FollowFallbacks],
        Relationships = [new RelationshipRule { PredicateRole = FallsBackToRole.Id }],
    };

    // The parcel registers the model declares, in name order so two reads try them the same
    // way. One with no usable declaration — no name, no reshape, or a span whose ends cross — covers
    // nowhere rather than failing every lookup beside it.
    public static IReadOnlyList<DeclaredLookup> ParcelLookups(SnapshotDocument snapshot) =>
        [.. snapshot.Bound(ParcelLookupRole)
            .Select(Lookup)
            .OfType<DeclaredLookup>()
            .OrderBy(lookup => lookup.Name, StringComparer.Ordinal)];

    // The place searches in the order the model asks them: the one the role binds, then each it falls back
    // to. A search with no reshape is one whose licence is not obtained yet and is left out; two bound to the
    // role leave the first by name to start from, the same shape as a vocabulary bound twice.
    public static IReadOnlyList<DeclaredLookup> PlaceSearches(SnapshotDocument snapshot) => Ordered(snapshot, PlaceSearchRole);

    public static IReadOnlyList<DeclaredLookup> BorderElevations(SnapshotDocument snapshot) => Ordered(snapshot, BorderElevationRole);

    private static IReadOnlyList<DeclaredLookup> Ordered(SnapshotDocument snapshot, Role role) =>
        snapshot.Bound(role).OrderBy(thing => thing.Name, StringComparer.Ordinal).FirstOrDefault() is { } first
            ? InFallbackOrder(snapshot, first)
            : [];

    private static IReadOnlyList<DeclaredLookup> InFallbackOrder(SnapshotDocument snapshot, SnapshotThing first)
    {
        var fallsBackTo = snapshot.Bound(FallsBackToRole).Select(thing => thing.Id).ToHashSet();
        var next = snapshot.Relationships
            .Where(edge => fallsBackTo.Contains(edge.PredicateId))
            .GroupBy(edge => edge.SubjectId)
            .ToDictionary(group => group.Key, group => group.First().TargetId);
        var things = snapshot.Things.DistinctBy(thing => thing.Id).ToDictionary(thing => thing.Id);

        var ordered = new List<DeclaredLookup>();
        var visited = new HashSet<Guid>();
        for (var at = first; at is not null && visited.Add(at.Id);
             at = next.TryGetValue(at.Id, out var following) ? things.GetValueOrDefault(following) : null)
        {
            if (Lookup(at) is { } usable) ordered.Add(usable);
        }
        return ordered;
    }

    private static DeclaredLookup? Lookup(SnapshotThing thing)
    {
        if (string.IsNullOrWhiteSpace(thing.Name)) return null;
        if (Text(thing, TransformProperty) is not { } transform) return null;

        return new DeclaredLookup(thing.Name, transform, Text(thing, AttributionProperty), Bounds(thing));
    }

    private static DeclaredBounds? Bounds(SnapshotThing thing)
    {
        if (Number(thing, SouthLatitudeProperty) is not { } south
            || Number(thing, NorthLatitudeProperty) is not { } north
            || Number(thing, WestLongitudeProperty) is not { } west
            || Number(thing, EastLongitudeProperty) is not { } east)
            return null;

        return south < north && west < east ? new DeclaredBounds(south, north, west, east) : null;
    }

    private static string? Text(SnapshotThing thing, string property) =>
        thing.StatedValue(property) is { } stated && stated.Value.ValueKind == JsonValueKind.String
            ? stated.Value.GetString()
            : null;

    private static double? Number(SnapshotThing thing, string property) =>
        thing.StatedValue(property) is { } stated && stated.Value.ValueKind == JsonValueKind.Number
            ? stated.Value.GetDouble()
            : null;
}
