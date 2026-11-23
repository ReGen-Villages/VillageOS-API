using vos.Service.Shared.Roles;

namespace vos.Service.Shared.Geometry;

public enum AreaMethod
{
    MeanSphere,
    Wgs84Ellipsoid,
}

public static class AreaMethods
{
    public static readonly IReadOnlyList<(Role Role, AreaMethod Method)> Implemented =
    [
        (LandIntakeRoles.MeanSphereAreaMethod, AreaMethod.MeanSphere),
        (LandIntakeRoles.Wgs84EllipsoidAreaMethod, AreaMethod.Wgs84Ellipsoid),
    ];
}

public static class BorderArea
{
    private const double MeanEarthRadiusMetres = 6371008.8;
    private const double Wgs84EquatorialRadiusMetres = 6378137.0;
    private const double Wgs84Flattening = 1 / 298.257223563;
    private const double SquareMetresPerHectare = 10_000;

    private static readonly double Eccentricity = Math.Sqrt(Wgs84Flattening * (2 - Wgs84Flattening));
    private static readonly double AuthalicAtThePole = Authalic(Math.PI / 2);
    private static readonly double EqualAreaRadiusMetres = Wgs84EquatorialRadiusMetres * Math.Sqrt(AuthalicAtThePole / 2);

    public static double Hectares(IReadOnlyList<(double Latitude, double Longitude)> corners, AreaMethod method) =>
        method switch
        {
            AreaMethod.MeanSphere => MeanSphereSquareMetres(corners) / SquareMetresPerHectare,
            AreaMethod.Wgs84Ellipsoid => Wgs84EllipsoidSquareMetres(corners) / SquareMetresPerHectare,
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "no formula measures with this method"),
        };

    private static double MeanSphereSquareMetres(IReadOnlyList<(double Latitude, double Longitude)> corners)
    {
        var total = 0.0;
        for (var corner = 0; corner < corners.Count; corner++)
        {
            var here = corners[corner];
            var next = corners[(corner + 1) % corners.Count];
            total += ToRadians(next.Longitude - here.Longitude)
                     * (2 + Math.Sin(ToRadians(here.Latitude)) + Math.Sin(ToRadians(next.Latitude)));
        }

        return Math.Abs(total) * MeanEarthRadiusMetres * MeanEarthRadiusMetres / 2;
    }

    // Each latitude moves to the sphere whose area equals the ellipsoid's, and the border's area there is the sum of
    // the spherical excess of a fan of triangles from its first corner, which counts a concave border's turns by sign.
    private static double Wgs84EllipsoidSquareMetres(IReadOnlyList<(double Latitude, double Longitude)> corners)
    {
        if (corners.Count < 3) return 0;

        var points = corners.Select(corner => OnTheEqualAreaSphere(corner.Latitude, corner.Longitude)).ToArray();
        var first = points[0];
        var excess = 0.0;
        for (var corner = 1; corner < points.Length - 1; corner++)
        {
            var (here, next) = (points[corner], points[corner + 1]);
            excess += 2 * Math.Atan2(
                Dot(first, Cross(here, next)),
                1 + Dot(first, here) + Dot(here, next) + Dot(next, first));
        }

        return Math.Abs(excess) * EqualAreaRadiusMetres * EqualAreaRadiusMetres;
    }

    private static (double X, double Y, double Z) OnTheEqualAreaSphere(double latitude, double longitude)
    {
        var equalAreaLatitude = Math.Asin(Math.Clamp(Authalic(ToRadians(latitude)) / AuthalicAtThePole, -1, 1));
        var radiansEast = ToRadians(longitude);
        return (Math.Cos(equalAreaLatitude) * Math.Cos(radiansEast), Math.Cos(equalAreaLatitude) * Math.Sin(radiansEast),
            Math.Sin(equalAreaLatitude));
    }

    private static double Authalic(double latitude)
    {
        var sine = Math.Sin(latitude);
        var squared = Eccentricity * Eccentricity;
        return (1 - squared) * (sine / (1 - squared * sine * sine)
                                - Math.Log((1 - Eccentricity * sine) / (1 + Eccentricity * sine)) / (2 * Eccentricity));
    }

    private static double Dot((double X, double Y, double Z) left, (double X, double Y, double Z) right) =>
        left.X * right.X + left.Y * right.Y + left.Z * right.Z;

    private static (double X, double Y, double Z) Cross((double X, double Y, double Z) left, (double X, double Y, double Z) right) =>
        (left.Y * right.Z - left.Z * right.Y, left.Z * right.X - left.X * right.Z, left.X * right.Y - left.Y * right.X);

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
