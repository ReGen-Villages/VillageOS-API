namespace vos.Service.Intake.Models;

// A position somebody clicked, asked about before any site exists — which is why the answer
// hangs off nothing and writes nothing.
public sealed record ParcelAsked(double? Latitude, double? Longitude);

public sealed record PlacesAsked(string? Query);

public sealed record ElevationAsked(IReadOnlyList<BoundaryPoint>? Boundary);

// A figure the provider held no height for stays unknown rather than nought.
public sealed record AnsweredElevation(
    double? ElevationMetres, double? ElevationMinimumMetres, double? ElevationMaximumMetres, string? Attribution)
{
    public ElevationRange? Range =>
        ElevationMinimumMetres is { } lowest && ElevationMaximumMetres is { } highest
            ? new ElevationRange(lowest, highest)
            : null;
}

// A site keeps its lowest and highest point as a pair, because one without the other describes no border.
public sealed record ElevationRange(double MinimumMetres, double MaximumMetres);

// The legal parcel a register holds at a position: its boundary as named pairs, and whose
// record it is — the register's own credit line, which the page that draws the boundary displays.
public sealed record AnsweredParcel(IReadOnlyList<BoundaryPoint> Boundary, string? Attribution);

public sealed record AnsweredPlace(string Name, double Latitude, double Longitude);

public sealed record AnsweredPlaces(IReadOnlyList<AnsweredPlace> Places, string? Attribution);

// A parcel-register registration as the model declares it: the name Tributary is asked to call,
// the reshape this service applies to the raw body, whose record the answer is, and the bounds it
// covers — a pair of latitude and longitude spans rather than a covers relationship, because a clicked position
// belongs to no site and a coverage walk has nowhere to start.
public sealed record DeclaredLookup(
    string Name, string Transform, string? Attribution, DeclaredBounds? Bounds)
{
    public bool Covers(double latitude, double longitude) =>
        Bounds is { } bounds
        && latitude >= bounds.SouthLatitude && latitude <= bounds.NorthLatitude
        && longitude >= bounds.WestLongitude && longitude <= bounds.EastLongitude;
}

public sealed record DeclaredBounds(
    double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude)
{
    public bool Overlaps(DeclaredBounds view) =>
        view.SouthLatitude <= NorthLatitude && view.NorthLatitude >= SouthLatitude
        && view.WestLongitude <= EastLongitude && view.EastLongitude >= WestLongitude;
}

// A register's plots for the map in view: answered only at its minimum zoom and closer, and for at most
// its stated number of plots a call.
public sealed record DeclaredPlotsLookup(DeclaredLookup Lookup, int MinimumZoom, int? MostPlots);

public sealed record PlotsAsked(
    double? SouthLatitude, double? WestLongitude, double? NorthLatitude, double? EastLongitude, int? Zoom);

// One plot as the register holds it: its outer ring, and the register's own identifier and area.
public sealed record AnsweredPlot(IReadOnlyList<BoundaryPoint> Boundary, string? Identifier, double? AreaHectares);

public sealed record AnsweredPlots(IReadOnlyList<AnsweredPlot> Plots, IReadOnlyList<string> Attributions);
