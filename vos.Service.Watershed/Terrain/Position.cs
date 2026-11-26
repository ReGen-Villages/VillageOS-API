namespace vos.Service.Watershed.Terrain;

public readonly record struct Position(double Latitude, double Longitude);

// The flat-earth factors the intake and the site-study pages use, close enough across one site.
public static class EarthDistances
{
    public const double MetresPerDegreeOfLatitude = 110574;
    public const double MetresPerDegreeOfLongitudeAtTheEquator = 111320;

    public static double MetresPerDegreeOfLongitudeAt(double latitude) =>
        MetresPerDegreeOfLongitudeAtTheEquator * Math.Cos(latitude * Math.PI / 180);
}
