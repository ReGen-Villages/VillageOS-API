using System.Globalization;
using System.Text.Json;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Services;

public enum TileOutcome { Served, NoSuchBasemap, ProviderUnavailable }

public sealed record ServedTile(TileOutcome Outcome, byte[]? Bytes = null, string? ContentType = null, TimeSpan? CacheLife = null);

// One tile of a basemap, fetched through the registration the route names — the broker's endpoint route
// forwards to the fetching service, which reaches the provider and keeps the bytes on disk for the
// registration's cache life — so a page is never handed the provider's address or any key.
public sealed class BasemapTileService(
    IntakeMyceliumClient mycelium,
    ISubscriptionClient subscriptions,
    string fetcherSubdomain,
    ILogger<BasemapTileService> logger)
{
    public async Task<ServedTile> TileAsync(string basemap, int z, int x, int y, CancellationToken cancellation)
    {
        var registration = await subscriptions.ReadAsync(
            BasemapTileReader.Selector(), snapshot => BasemapTileReader.Named(snapshot, basemap), logger, cancellation);
        if (registration is null) return new ServedTile(TileOutcome.NoSuchBasemap);

        var body = await mycelium.CallEndpointAsync(fetcherSubdomain, new
        {
            endpointName = registration.Name,
            addressParameters = new Dictionary<string, string>
            {
                ["z"] = z.ToString(CultureInfo.InvariantCulture),
                ["x"] = x.ToString(CultureInfo.InvariantCulture),
                ["y"] = y.ToString(CultureInfo.InvariantCulture),
            },
        }, cancellation);
        if (body is null) return new ServedTile(TileOutcome.ProviderUnavailable);

        // The fetcher answers bytes in a base64 envelope, since its answer travels as JSON like every
        // other; the page is given the bytes back with the type the provider served them as.
        using var envelope = JsonDocument.Parse(body);
        if (!envelope.RootElement.TryGetProperty("dataBase64", out var data) || data.ValueKind != JsonValueKind.String)
            return new ServedTile(TileOutcome.ProviderUnavailable);
        var contentType = envelope.RootElement.TryGetProperty("contentType", out var type) && type.ValueKind == JsonValueKind.String
            ? type.GetString()!
            : "application/octet-stream";
        return new ServedTile(TileOutcome.Served, Convert.FromBase64String(data.GetString()!), contentType, registration.CacheLife);
    }
}
