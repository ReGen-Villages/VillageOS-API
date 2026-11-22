namespace vos.Service.Phyllotaxis.Placement;

// Degrees to metres on the WGS84 ellipsoid at one latitude, which is exact enough across a site of a few kilometres.
public static class LocalFrame
{
    private const double EquatorialRadiusMetres = 6378137.0;
    private const double Flattening = 1 / 298.257223563;

    public static double MetresPerDegreeOfLatitude(double latitudeDegrees)
    {
        throw new NotImplementedException();
    }

    public static double MetresPerDegreeOfLongitude(double latitudeDegrees)
    {
        throw new NotImplementedException();
    }

    public static Point ToMetres((double Latitude, double Longitude) origin, (double Latitude, double Longitude) point)
    {
        throw new NotImplementedException();
    }
}
