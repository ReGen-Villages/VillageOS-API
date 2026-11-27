using System.Globalization;

namespace vos.Service.Watershed.LandCover;

public static class WorldCoverTiles
{
    public const int DegreesASide = 3;

    public static IReadOnlyList<string> Touching(double south, double west, double north, double east) =>
    [
        .. from southEdge in Edges(south, north)
           from westEdge in Edges(west, east)
           select NameOf(southEdge, westEdge),
    ];

    private static IEnumerable<int> Edges(double from, double to)
    {
        for (var edge = SouthOrWestEdgeOf(from); edge <= SouthOrWestEdgeOf(to); edge += DegreesASide)
            yield return edge;
    }

    private static int SouthOrWestEdgeOf(double degrees) => (int)Math.Floor(degrees / DegreesASide) * DegreesASide;

    private static string NameOf(int south, int west) => string.Create(CultureInfo.InvariantCulture,
        $"{(south < 0 ? 'S' : 'N')}{Math.Abs(south):00}{(west < 0 ? 'W' : 'E')}{Math.Abs(west):000}");
}
