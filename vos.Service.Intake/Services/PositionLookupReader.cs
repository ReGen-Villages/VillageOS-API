using System.Text.Json;
using vos.Service.Intake.Models;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Services;

/// <summary>
/// The lookups a page makes before any site exists — the legal parcel at a clicked position, and place
/// names for what somebody typed — read from the model that registers them.
/// </summary>
/// <remarks>
/// Each registration is found by the mark it carries, as every vocabulary this service reads is. What is
/// read off it is what this service needs and nothing Tributary already resolves for itself: the name a
/// call is forwarded under, the reshape this service applies to the raw body — under a property of its
/// own, because Tributary ingests a <c>responseTransform</c> and a position that is not yet a site has
/// nothing to ingest onto — whose record the answer is, and for a parcel register the bounds it covers.
/// Bounds are a pair of spans rather than a <c>covers</c> edge: a clicked position belongs to no site,
/// so the coverage walk that selects discovery sources has nowhere to start.
/// </remarks>
public static class PositionLookupReader
{
    public const string ParcelLookupFlag = "__IsParcelLookup";
    public const string PlaceSearchFlag = "__IsPlaceSearch";

    public const string TransformProperty = "lookupTransform";
    public const string AttributionProperty = "attribution";
    public const string SouthLatitudeProperty = "boundsSouthLatitude";
    public const string NorthLatitudeProperty = "boundsNorthLatitude";
    public const string WestLongitudeProperty = "boundsWestLongitude";
    public const string EastLongitudeProperty = "boundsEastLongitude";

    public static SubscriptionSelector Selector() => new()
    {
        MarkedArchetypes = [ParcelLookupFlag, PlaceSearchFlag],
    };

    /// <summary>The parcel registers the model declares, in name order so two reads try them the same
    /// way. One with no usable declaration — no name, no reshape, or a span whose ends cross — covers
    /// nowhere rather than failing every lookup beside it.</summary>
    public static IReadOnlyList<DeclaredLookup> ParcelLookups(SnapshotDocument snapshot) =>
        [.. snapshot.Things
            .Where(thing => thing.CarriesFlag(ParcelLookupFlag))
            .Select(Lookup)
            .OfType<DeclaredLookup>()
            .OrderBy(lookup => lookup.Name, StringComparer.Ordinal)];

    /// <summary>The one place search the model declares, or null where it declares none. Two would leave
    /// nothing able to say which a query goes to, so the first by name answers and the rest are dead
    /// declarations a deployment should notice — the same shape as a vocabulary carried twice.</summary>
    public static DeclaredLookup? PlaceSearch(SnapshotDocument snapshot) =>
        snapshot.Things
            .Where(thing => thing.CarriesFlag(PlaceSearchFlag))
            .Select(Lookup)
            .OfType<DeclaredLookup>()
            .OrderBy(lookup => lookup.Name, StringComparer.Ordinal)
            .FirstOrDefault();

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
