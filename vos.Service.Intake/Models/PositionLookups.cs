namespace vos.Service.Intake.Models;

/// <summary>A position somebody clicked, asked about before any site exists — which is why the answer
/// hangs off nothing and writes nothing.</summary>
public sealed record ParcelAsked(double? Latitude, double? Longitude);

/// <summary>What somebody typed while looking for their land.</summary>
public sealed record PlacesAsked(string? Query);

/// <summary>The legal parcel a register holds at a position: its boundary as named pairs, and whose
/// record it is — the register's own credit line, which the page that draws the boundary displays.</summary>
public sealed record AnsweredParcel(IReadOnlyList<BoundaryPoint> Boundary, string? Attribution);

public sealed record AnsweredPlace(string Name, double Latitude, double Longitude);

public sealed record AnsweredPlaces(IReadOnlyList<AnsweredPlace> Places, string? Attribution);

/// <summary>A parcel-register registration as the model declares it: the name Tributary is asked to call,
/// the reshape this service applies to the raw body, whose record the answer is, and the bounds it
/// covers — a pair of latitude and longitude spans rather than a covers edge, because a clicked position
/// belongs to no site and a coverage walk has nowhere to start.</summary>
public sealed record DeclaredLookup(
    string Name, string Transform, string? Attribution, DeclaredBounds? Bounds)
{
    public bool Covers(double latitude, double longitude) =>
        Bounds is { } bounds
        && latitude >= bounds.SouthLatitude && latitude <= bounds.NorthLatitude
        && longitude >= bounds.WestLongitude && longitude <= bounds.EastLongitude;
}

public sealed record DeclaredBounds(
    double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude);
