using System.Text.Json;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Services;

/// <summary>A tile registration the model marks as a basemap's: its name, which the public route names,
/// and how long a served tile may be cached — the registration's own cache life, so the page and the
/// fetcher's disk keep a tile for the same time.</summary>
public sealed record DeclaredTileRegistration(string Name, TimeSpan? CacheLife);

/// <summary>
/// Reads the tile registrations a page may fetch through this service. Only a registration carrying the
/// mark is served: the route names a registration, and serving any other would make this service a
/// public proxy for every provider the catalogue registers.
/// </summary>
public static class BasemapTileReader
{
    public const string RegistrationFlag = "__IsBasemapTileRegistration";
    public const string CacheLifeProperty = "cacheTtl";

    public static SubscriptionSelector Selector() => new() { MarkedArchetypes = [RegistrationFlag] };

    public static DeclaredTileRegistration? Named(SnapshotDocument snapshot, string name) =>
        snapshot.Things
            .Where(thing => thing.CarriesFlag(RegistrationFlag)
                            && string.Equals(thing.Name, name, StringComparison.Ordinal))
            .Select(thing => new DeclaredTileRegistration(thing.Name!, CacheLife(thing)))
            .FirstOrDefault();

    private static TimeSpan? CacheLife(SnapshotThing thing) =>
        thing.StatedValue(CacheLifeProperty) is { } stated
        && stated.Value.ValueKind == JsonValueKind.Number
        && stated.Value.TryGetDouble(out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : null;
}
