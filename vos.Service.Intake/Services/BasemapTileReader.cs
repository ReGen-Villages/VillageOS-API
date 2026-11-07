using System.Text.Json;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Services;

// The tile registration the model's role binds: its name, which the public route names,
// and how long a served tile may be cached — the registration's own cache life, so the page and the
// fetcher's disk keep a tile for the same time.
public sealed record DeclaredTileRegistration(string Name, TimeSpan? CacheLife);

// Reads the tile registration a page may fetch through this service. Only the registration the role
// binds is served: the route names a registration, and serving any other would make this service a
// public proxy for every provider the catalogue registers.
public static class BasemapTileReader
{
    public static readonly Role RegistrationRole = new("basemapTileRegistration", new("e1f400e6-e3b6-51a2-b918-e845af5be46a"));
    public const string CacheLifeProperty = "cacheTtl";

    public static SubscriptionSelector Selector() => new()
    {
        Ids = [RegistrationRole.Id],
        Names = [RoleBindings.BindsPredicateName],
        BoundArchetypes = [RegistrationRole.Id],
        Traverse = [RoleBindings.FollowBindings],
    };

    public static DeclaredTileRegistration? Named(SnapshotDocument snapshot, string name) =>
        snapshot.Bound(RegistrationRole)
            .Where(thing => string.Equals(thing.Name, name, StringComparison.Ordinal))
            .Select(thing => new DeclaredTileRegistration(thing.Name!, CacheLife(thing)))
            .FirstOrDefault();

    private static TimeSpan? CacheLife(SnapshotThing thing) =>
        thing.StatedValue(CacheLifeProperty) is { } stated
        && stated.Value.ValueKind == JsonValueKind.Number
        && stated.Value.TryGetDouble(out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : null;
}
