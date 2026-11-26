namespace vos.Service.Watershed.Terrain;

public sealed record ProfileLine(
    IReadOnlyList<Position> Points, double SpacingMetres, double LatitudeStepDegrees, double LongitudeStepDegrees,
    double SiteFromMetres, double SiteToMetres);

// Each line crosses the middle of the border's box and reaches past the site by the same margin the height grid takes.
public static class ProfileLines
{
    public const int PointsAlongALine = 50;
    public const double ShortestHalfLengthMetres = 250;

    public static (ProfileLine WestToEast, ProfileLine SouthToNorth) Through(IReadOnlyList<Position> border)
    {
        var (south, north) = (border.Min(corner => corner.Latitude), border.Max(corner => corner.Latitude));
        var (west, east) = (border.Min(corner => corner.Longitude), border.Max(corner => corner.Longitude));
        var middle = new Position((south + north) / 2, (west + east) / 2);
        var metresPerDegreeOfLongitude = EarthDistances.MetresPerDegreeOfLongitudeAt(middle.Latitude);

        var halfWidth = (east - west) / 2 * metresPerDegreeOfLongitude;
        var halfHeight = (north - south) / 2 * EarthDistances.MetresPerDegreeOfLatitude;
        return (
            Line(halfWidth, spacing => (0, spacing / metresPerDegreeOfLongitude), middle, metresPerDegreeOfLongitude),
            Line(halfHeight, spacing => (spacing / EarthDistances.MetresPerDegreeOfLatitude, 0), middle, metresPerDegreeOfLongitude));
    }

    private static ProfileLine Line(
        double siteHalfLength, Func<double, (double Latitude, double Longitude)> stepOf, Position middle, double metresPerDegreeOfLongitude)
    {
        var halfLength = Math.Max(siteHalfLength * HeightGridLayout.MarginOverTheBox, ShortestHalfLengthMetres);
        var spacing = 2 * halfLength / (PointsAlongALine - 1);
        var (latitudeStep, longitudeStep) = stepOf(spacing);
        var (startLatitude, startLongitude) = (middle.Latitude - (PointsAlongALine - 1) / 2.0 * latitudeStep,
            middle.Longitude - (PointsAlongALine - 1) / 2.0 * longitudeStep);
        var points = Enumerable.Range(0, PointsAlongALine)
            .Select(index => new Position(startLatitude + index * latitudeStep, startLongitude + index * longitudeStep)).ToArray();

        // A stored grid refuses a step of nought, so the step across the line is the one a line that way would take.
        return new ProfileLine(points, spacing,
            latitudeStep > 0 ? latitudeStep : spacing / EarthDistances.MetresPerDegreeOfLatitude,
            longitudeStep > 0 ? longitudeStep : spacing / metresPerDegreeOfLongitude,
            halfLength - siteHalfLength, halfLength + siteHalfLength);
    }
}
