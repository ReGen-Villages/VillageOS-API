using vos.Service.Watershed.Terrain;

namespace vos.Service.Watershed.Tests;

public static class MadeUpBorders
{
    public const double Latitude = 40.0;
    public const double Longitude = -30.0;

    public static double MetresPerDegreeOfLongitude => EarthDistances.MetresPerDegreeOfLongitudeAt(Latitude);

    public static IReadOnlyList<Position> Rectangle(double widthMetres, double heightMetres)
    {
        var halfLatitude = heightMetres / 2 / EarthDistances.MetresPerDegreeOfLatitude;
        var halfLongitude = widthMetres / 2 / MetresPerDegreeOfLongitude;
        return
        [
            new(Latitude - halfLatitude, Longitude - halfLongitude),
            new(Latitude - halfLatitude, Longitude + halfLongitude),
            new(Latitude + halfLatitude, Longitude + halfLongitude),
            new(Latitude + halfLatitude, Longitude - halfLongitude),
        ];
    }
}
