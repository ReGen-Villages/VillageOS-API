using System.Globalization;
using System.Text.Json;
using vos.Service.Intake.Models;
using vos.Service.Shared;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Services;

/// <summary>What a position lookup came to: an answer, nothing available, or a register that could not
/// be asked. Three outcomes rather than exceptions, because the first two are answers a stranger's
/// browser acts on and only the last is the deployment's problem.</summary>
public enum LookupOutcome { Found, NothingAvailable, ProviderUnavailable }

public sealed record ParcelLookup(LookupOutcome Outcome, AnsweredParcel? Parcel = null);

public sealed record PlacesLookup(LookupOutcome Outcome, AnsweredPlaces? Places = null);

/// <summary>
/// The two lookups a page makes before any site exists, answered from the registrations the model
/// declares. The outbound call goes through the broker's endpoint-forward route to the fetching
/// service, which owns addresses, placeholder filling and outbound identification — a second HTTP path
/// here would be a second mapping to keep right. What comes back is the provider's raw body, because
/// the registration carries no <c>responseTransform</c>; the reshape this service applies is the
/// registration's own <c>lookupTransform</c>.
/// </summary>
public sealed class PositionLookupService(
    IntakeMyceliumClient mycelium,
    ISubscriptionClient subscriptions,
    string fetcherSubdomain,
    ILogger<PositionLookupService> logger)
{
    /// <summary>One wording for a register nothing registered, none covering the position, and none
    /// holding a parcel there: the caller does the same thing in all three — draws the boundary — and
    /// the form options already say whether the capability exists at all.</summary>
    public const string NoParcelAvailable =
        "No parcel boundary is available at this position. Draw the boundary instead.";

    public const string NoSearchAvailable = "No place search is available here.";

    public async Task<ParcelLookup> ParcelAtAsync(
        double latitude, double longitude, CancellationToken cancellation)
    {
        var registered = await subscriptions.ReadAsync(
            PositionLookupReader.Selector(), PositionLookupReader.ParcelLookups, logger, cancellation);
        var covering = registered.Where(lookup => lookup.Covers(latitude, longitude)).ToList();
        if (covering.Count == 0) return new ParcelLookup(LookupOutcome.NothingAvailable);

        var asked = 0;
        foreach (var register in covering)
        {
            var body = await mycelium.CallEndpointAsync(fetcherSubdomain, new
            {
                endpointName = register.Name,
                addressParameters = new Dictionary<string, string>
                {
                    ["latitude"] = latitude.ToString(CultureInfo.InvariantCulture),
                    ["longitude"] = longitude.ToString(CultureInfo.InvariantCulture),
                },
            }, cancellation);
            if (body is null) continue;
            asked++;

            // The first covering register that answers decides: registers do not share territory, so a
            // second opinion is not a better one, and asking on costs a public register a call for an
            // answer already given.
            if (Boundary(register, body) is { } boundary)
                return new ParcelLookup(
                    LookupOutcome.Found, new AnsweredParcel(boundary, register.Attribution));
            return new ParcelLookup(LookupOutcome.NothingAvailable);
        }

        // Every covering register failed to answer — the deployment's problem, not the position's.
        return new ParcelLookup(
            asked == 0 ? LookupOutcome.ProviderUnavailable : LookupOutcome.NothingAvailable);
    }

    public async Task<PlacesLookup> PlacesAsync(string query, CancellationToken cancellation)
    {
        var search = await subscriptions.ReadAsync(
            PositionLookupReader.Selector(), PositionLookupReader.PlaceSearch, logger, cancellation);
        if (search is null) return new PlacesLookup(LookupOutcome.NothingAvailable);

        var body = await mycelium.CallEndpointAsync(fetcherSubdomain, new
        {
            endpointName = search.Name,
            addressParameters = new Dictionary<string, string> { ["query"] = query },
        }, cancellation);
        if (body is null) return new PlacesLookup(LookupOutcome.ProviderUnavailable);

        return new PlacesLookup(
            LookupOutcome.Found, new AnsweredPlaces(Places(search, body), search.Attribution));
    }

    private IReadOnlyList<BoundaryPoint>? Boundary(DeclaredLookup register, string body)
    {
        if (Reshaped(register, body) is not { } answered) return null;
        using (answered)
        {
            if (answered.RootElement.ValueKind != JsonValueKind.Object
                || !answered.RootElement.TryGetProperty("boundary", out var ring)
                || ring.ValueKind != JsonValueKind.Array)
                return null;

            var corners = new List<BoundaryPoint>();
            foreach (var corner in ring.EnumerateArray())
            {
                if (Coordinate(corner, "latitude", 90) is not { } cornerLatitude
                    || Coordinate(corner, "longitude", 180) is not { } cornerLongitude
                    || corners.Count == SubmissionLimits.MostBoundaryCorners)
                {
                    // A register answering a shape this service would refuse as a submission is a
                    // registration to fix, not a boundary to pass along.
                    logger.LogWarning(
                        "The register '{Register}' answered a boundary outside what a parcel takes",
                        register.Name);
                    return null;
                }
                corners.Add(new BoundaryPoint { Latitude = cornerLatitude, Longitude = cornerLongitude });
            }
            return corners.Count >= 3 ? corners : null;
        }
    }

    private IReadOnlyList<AnsweredPlace> Places(DeclaredLookup search, string body)
    {
        if (Reshaped(search, body) is not { } answered) return [];
        using (answered)
        {
            if (answered.RootElement.ValueKind != JsonValueKind.Object
                || !answered.RootElement.TryGetProperty("places", out var found)
                || found.ValueKind != JsonValueKind.Array)
                return [];

            var places = new List<AnsweredPlace>();
            foreach (var place in found.EnumerateArray())
            {
                if (place.ValueKind != JsonValueKind.Object
                    || !place.TryGetProperty("name", out var name)
                    || name.ValueKind != JsonValueKind.String
                    || Coordinate(place, "latitude", 90) is not { } placeLatitude
                    || Coordinate(place, "longitude", 180) is not { } placeLongitude)
                    continue;
                places.Add(new AnsweredPlace(name.GetString()!, placeLatitude, placeLongitude));
            }
            return places;
        }
    }

    // A reshape that cannot be applied is a registration to fix: the caller is a stranger's browser and
    // is told nothing about it, the way an unseeded model is answered.
    private JsonDocument? Reshaped(DeclaredLookup lookup, string body)
    {
        string answered;
        try
        {
            answered = new JsonataTransform(lookup.Transform).Eval(body);
        }
        catch (Exception error)
        {
            logger.LogError(error, "The reshape on lookup '{Lookup}' could not be applied", lookup.Name);
            throw new ModelNotSeededError(
                $"the reshape on lookup '{lookup.Name}' could not be applied to the provider's answer.");
        }

        if (string.IsNullOrWhiteSpace(answered)) return null;
        try
        {
            return JsonDocument.Parse(answered);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static double? Coordinate(JsonElement holder, string name, double span) =>
        holder.ValueKind == JsonValueKind.Object
        && holder.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.GetDouble() is var read
        && read >= -span && read <= span
            ? read
            : null;
}
