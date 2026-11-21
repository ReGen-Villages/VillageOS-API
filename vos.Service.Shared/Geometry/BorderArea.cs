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
    private const double SquareMetresPerHectare = 10_000;

    public static double Hectares(IReadOnlyList<(double Latitude, double Longitude)> corners, AreaMethod method)
    {
        var total = 0.0;
        for (var corner = 0; corner < corners.Count; corner++)
        {
            var here = corners[corner];
            var next = corners[(corner + 1) % corners.Count];
            total += ToRadians(next.Longitude - here.Longitude)
                     * (2 + Math.Sin(ToRadians(here.Latitude)) + Math.Sin(ToRadians(next.Latitude)));
        }

        return Math.Abs(total) * MeanEarthRadiusMetres * MeanEarthRadiusMetres / 2 / SquareMetresPerHectare;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
