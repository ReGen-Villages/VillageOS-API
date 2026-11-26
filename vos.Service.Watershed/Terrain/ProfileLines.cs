namespace vos.Service.Watershed.Terrain;

public sealed record ProfileLine(
    IReadOnlyList<Position> Points, double SpacingMetres, double LatitudeStepDegrees, double LongitudeStepDegrees,
    double SiteFromMetres, double SiteToMetres);

// Each line crosses the middle of the border's box and reaches past the site by the same margin the height grid takes.
public static class ProfileLines
{
    public const int PointsAlongALine = 50;
    public const double ShortestHalfLengthMetres = 250;

    public static (ProfileLine WestToEast, ProfileLine SouthToNorth) Through(IReadOnlyList<Position> border) =>
        throw new NotImplementedException();
}
