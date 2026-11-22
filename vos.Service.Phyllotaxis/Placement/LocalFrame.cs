namespace vos.Service.Phyllotaxis.Placement;

// Degrees to metres on the WGS84 ellipsoid at one latitude, which is exact enough across a site of a few kilometres.
public static class LocalFrame
{
    private const double EquatorialRadiusMetres = 6378137.0;
    private const double Flattening = 1 / 298.257223563;
    private const double EccentricitySquared = Flattening * (2 - Flattening);

    public static double MetresPerDegreeOfLatitude(double latitudeDegrees)
    {
        var meridianRadius = EquatorialRadiusMetres * (1 - EccentricitySquared) / Math.Pow(CurvatureTerm(latitudeDegrees), 1.5);
        return meridianRadius * Math.PI / 180;
    }

    public static double MetresPerDegreeOfLongitude(double latitudeDegrees)
    {
        var primeVerticalRadius = EquatorialRadiusMetres / Math.Sqrt(CurvatureTerm(latitudeDegrees));
        return primeVerticalRadius * Math.Cos(Radians(latitudeDegrees)) * Math.PI / 180;
    }

    public static Point ToMetres((double Latitude, double Longitude) origin, (double Latitude, double Longitude) point) =>
        new(
            (point.Longitude - origin.Longitude) * MetresPerDegreeOfLongitude(origin.Latitude),
            (point.Latitude - origin.Latitude) * MetresPerDegreeOfLatitude(origin.Latitude));

    private static double CurvatureTerm(double latitudeDegrees)
    {
        var sine = Math.Sin(Radians(latitudeDegrees));
        return 1 - EccentricitySquared * sine * sine;
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180;
}
