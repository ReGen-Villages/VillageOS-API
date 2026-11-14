using System.Globalization;
using System.Text.Json;
using vos.Service.Intake.Models;
using vos.Service.Shared;

namespace vos.Service.Intake.Services;

// What a position lookup came to: an answer, nothing available, or a register that could not
// be asked. Three outcomes rather than exceptions, because the first two are answers a stranger's
// browser acts on and only the last is the deployment's problem.
public enum LookupOutcome { Found, NothingAvailable, ProviderUnavailable }

public sealed record ParcelLookup(LookupOutcome Outcome, AnsweredParcel? Parcel = null);

public sealed record PlacesLookup(LookupOutcome Outcome, AnsweredPlaces? Places = null);

public sealed record ElevationLookup(LookupOutcome Outcome, AnsweredElevation? Elevation = null);

public sealed record PlotsLookup(LookupOutcome Outcome, AnsweredPlots? Plots = null);

// The lookups a page makes before any site exists, answered from the registrations the model
// declares. The outbound call goes through the broker's endpoint-forward route to the fetching
// service, which owns addresses, placeholder filling and outbound identification — a second HTTP path
// here would be a second mapping to keep right. What comes back is the provider's raw body, because
// the registration carries no responseTransform; the reshape this service applies is the
// registration's own lookupTransform.
public sealed class PositionLookupService(
    IntakeMyceliumClient mycelium,
    HeldDeclarations declarations,
    string fetcherSubdomain,
    ILogger<PositionLookupService> logger,
    ModelClock clock)
{
    // One wording for a register nothing registered, none covering the position, and none
    // holding a parcel there: the caller does the same thing in all three — draws the boundary — and
    // the form options already say whether the capability exists at all.
    public const string NoParcelAvailable =
        "No parcel boundary is available at this position. Draw the boundary instead.";

    public const string NoSearchAvailable = "No place search is available here.";

    public const string NoElevationAvailable = "No elevation is available for this border.";

    public const string NoPlotsAvailable = "No register's plots are available for this view.";

    // Every register whose box overlaps the view and that answers at this zoom is asked, because a view can
    // straddle two registers' territory; their plots are answered together, each register credited once.
    public async Task<PlotsLookup> PlotsInViewAsync(DeclaredBounds view, int zoom, CancellationToken cancellation)
    {
        var registers = await declarations.ReadAsync(
            PositionLookupReader.PlotsSelector(), PositionLookupReader.PlotsInViewLookups, cancellation);
        var asking = registers
            .Where(register => register.Lookup.Bounds!.Overlaps(view) && zoom >= register.MinimumZoom)
            .ToList();
        if (asking.Count == 0) return new PlotsLookup(LookupOutcome.NothingAvailable);

        var plots = new List<AnsweredPlot>();
        var credits = new List<string>();
        var answered = 0;
        foreach (var register in asking)
        {
            var body = await mycelium.CallEndpointAsync(fetcherSubdomain, new
            {
                endpointName = register.Lookup.Name,
                addressParameters = new Dictionary<string, string>
                {
                    ["southLatitude"] = view.SouthLatitude.ToString(CultureInfo.InvariantCulture),
                    ["westLongitude"] = view.WestLongitude.ToString(CultureInfo.InvariantCulture),
                    ["northLatitude"] = view.NorthLatitude.ToString(CultureInfo.InvariantCulture),
                    ["eastLongitude"] = view.EastLongitude.ToString(CultureInfo.InvariantCulture),
                },
            }, cancellation);
            if (body is null) continue;
            answered++;

            var found = Plots(register, body);
            plots.AddRange(found);
            if (found.Count > 0 && register.Lookup.Attribution is { } credit) credits.Add(credit);
        }

        return answered == 0
            ? new PlotsLookup(LookupOutcome.ProviderUnavailable)
            : new PlotsLookup(LookupOutcome.Found, new AnsweredPlots(plots, credits));
    }

    // The provider's limit on points a call, less the one the centre takes.
    public const int MostElevationCorners = 99;

    public async Task<ElevationLookup> ElevationAsync(
        IReadOnlyList<BoundaryPoint> border, CancellationToken cancellation)
    {
        var lookups = await declarations.ReadAsync(
            PositionLookupReader.Selector(), PositionLookupReader.BorderElevations, cancellation);
        if (lookups.Count == 0) return new ElevationLookup(LookupOutcome.NothingAvailable);

        var centre = BoxCentre(border);
        string Pair(BoundaryPoint point) => string.Create(CultureInfo.InvariantCulture, $"{point.Latitude},{point.Longitude}");
        var parameters = new Dictionary<string, string>
        {
            ["locations"] = string.Join('|', SampledCorners(border).Prepend(centre).Select(Pair)),
            ["latitude"] = centre.Latitude.ToString(CultureInfo.InvariantCulture),
            ["longitude"] = centre.Longitude.ToString(CultureInfo.InvariantCulture),
        };

        var asked = 0;
        foreach (var lookup in lookups)
        {
            var body = await mycelium.CallEndpointAsync(fetcherSubdomain, new
            {
                endpointName = lookup.Name,
                addressParameters = parameters,
            }, cancellation);
            if (body is null) continue;
            asked++;
            if (Elevation(lookup, body) is { } answered) return new ElevationLookup(LookupOutcome.Found, answered);
        }

        return new ElevationLookup(asked == 0 ? LookupOutcome.ProviderUnavailable : LookupOutcome.NothingAvailable);
    }

    // The centre of the border's box, as the Location page shows it, rather than its centre of area.
    public static BoundaryPoint BoxCentre(IReadOnlyList<BoundaryPoint> border) => new()
    {
        Latitude = (border.Min(point => point.Latitude) + border.Max(point => point.Latitude)) / 2,
        Longitude = (border.Min(point => point.Longitude) + border.Max(point => point.Longitude)) / 2,
    };

    public static IReadOnlyList<BoundaryPoint> SampledCorners(IReadOnlyList<BoundaryPoint> border) =>
        border.Count <= MostElevationCorners
            ? border
            : [.. Enumerable.Range(0, MostElevationCorners).Select(index => border[index * border.Count / MostElevationCorners])];

    private AnsweredElevation? Elevation(DeclaredLookup lookup, string body)
    {
        if (Reshaped(lookup, body) is not { } answered) return null;
        using (answered)
        {
            if (answered.RootElement.ValueKind != JsonValueKind.Object) return null;
            double? Metres(string name) =>
                answered.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
                    ? value.GetDouble()
                    : null;

            var centre = Metres("elevationMetres");
            var lowest = Metres("elevationMinimumMetres");
            var highest = Metres("elevationMaximumMetres");
            return centre is null && lowest is null && highest is null
                ? null
                : new AnsweredElevation(centre, lowest, highest, lookup.Attribution);
        }
    }

    public async Task<ParcelLookup> ParcelAtAsync(
        double latitude, double longitude, CancellationToken cancellation)
    {
        var registered = await declarations.ReadAsync(
            PositionLookupReader.Selector(), PositionLookupReader.ParcelLookups, cancellation);
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
        var searches = await declarations.ReadAsync(
            PositionLookupReader.Selector(), PositionLookupReader.PlaceSearches, cancellation);
        if (searches.Count == 0) return new PlacesLookup(LookupOutcome.NothingAvailable);

        // A search that is refused, cannot be reached or finds nothing hands the query to the next one in the
        // model's order; finding nothing anywhere is still an answer, unlike every search being unreachable.
        AnsweredPlaces? foundNothing = null;
        foreach (var search in searches)
        {
            var body = await mycelium.CallEndpointAsync(fetcherSubdomain, new
            {
                endpointName = search.Name,
                addressParameters = new Dictionary<string, string> { ["query"] = query },
            }, cancellation);
            if (body is null) continue;

            var places = Places(search, body);
            if (places.Count > 0) return new PlacesLookup(LookupOutcome.Found, new AnsweredPlaces(places, search.Attribution));
            foundNothing ??= new AnsweredPlaces(places, search.Attribution);
        }

        return foundNothing is null
            ? new PlacesLookup(LookupOutcome.ProviderUnavailable)
            : new PlacesLookup(LookupOutcome.Found, foundNothing);
    }

    private IReadOnlyList<BoundaryPoint>? Boundary(DeclaredLookup register, string body)
    {
        if (Reshaped(register, body) is not { } answered) return null;
        using (answered)
        {
            return answered.RootElement.ValueKind == JsonValueKind.Object
                   && answered.RootElement.TryGetProperty("boundary", out var ring)
                ? Ring(register, ring)
                : null;
        }
    }

    private IReadOnlyList<BoundaryPoint>? Ring(DeclaredLookup register, JsonElement ring)
    {
        if (ring.ValueKind != JsonValueKind.Array) return null;

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

    private IReadOnlyList<AnsweredPlot> Plots(DeclaredPlotsLookup register, string body)
    {
        if (Reshaped(register.Lookup, body) is not { } answered) return [];
        using (answered)
        {
            if (answered.RootElement.ValueKind != JsonValueKind.Object
                || !answered.RootElement.TryGetProperty("plots", out var found)
                || found.ValueKind != JsonValueKind.Array)
                return [];

            var plots = new List<AnsweredPlot>();
            foreach (var plot in found.EnumerateArray())
            {
                if (plots.Count == register.MostPlots) break;
                if (plot.ValueKind != JsonValueKind.Object
                    || !plot.TryGetProperty("boundary", out var ring)
                    || Ring(register.Lookup, ring) is not { } boundary)
                    continue;
                plots.Add(new AnsweredPlot(
                    boundary,
                    plot.TryGetProperty("identifier", out var identifier) && identifier.ValueKind == JsonValueKind.String
                        ? identifier.GetString()
                        : null,
                    plot.TryGetProperty("areaHectares", out var area) && area.ValueKind == JsonValueKind.Number
                        ? area.GetDouble()
                        : null));
            }
            return plots;
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
            answered = new JsonataTransform(lookup.Transform).Eval(body, clock);
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
