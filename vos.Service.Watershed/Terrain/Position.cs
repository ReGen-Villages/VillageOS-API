namespace vos.Service.Watershed.Terrain;

public readonly record struct Position(double Latitude, double Longitude);

public static class EarthDistances
{
    public const double MetresPerDegreeOfLatitude = 110574;
    public const double MetresPerDegreeOfLongitudeAtTheEquator = 111320;

    public static double MetresPerDegreeOfLongitudeAt(double latitude) =>
        MetresPerDegreeOfLongitudeAtTheEquator * Math.Cos(latitude * Math.PI / 180);
}
