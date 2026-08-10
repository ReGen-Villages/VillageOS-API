using System.Text;
using vos.Service.Intake.Models;

namespace vos.Service.Intake.Helpers;

/// <summary>
/// What a drawn boundary encloses, and how it is stored.
///
/// The area is computed on the sphere. Treating latitude and longitude as flat coordinates looks right
/// near the equator and is meaningfully wrong away from it — a boundary in northern Europe would come out
/// close to twice its real size, and every downstream figure is proportional to the area.
/// </summary>
public static class BoundaryGeometry
{
    private const double EarthRadiusMetres = 6371008.8;
    private const double SquareMetresPerHectare = 10_000;

    public static double MeasureHectares(IReadOnlyList<BoundaryPoint> boundary)
    {
        var total = 0.0;
        for (var corner = 0; corner < boundary.Count; corner++)
        {
            var here = boundary[corner];
            var next = boundary[(corner + 1) % boundary.Count];
            total += ToRadians(next.Longitude - here.Longitude)
                     * (2 + Math.Sin(ToRadians(here.Latitude)) + Math.Sin(ToRadians(next.Latitude)));
        }

        var squareMetres = Math.Abs(total) * EarthRadiusMetres * EarthRadiusMetres / 2;
        return squareMetres / SquareMetresPerHectare;
    }

    /// <summary>The boundary as a GeoJSON polygon: longitude before latitude, and the first corner repeated
    /// at the end to close the ring, as that format requires.</summary>
    public static string ToGeoJson(IReadOnlyList<BoundaryPoint> boundary)
    {
        var ring = new StringBuilder();
        foreach (var corner in boundary)
            ring.Append(ring.Length == 0 ? "" : ",").Append(Corner(corner));
        if (boundary[^1] != boundary[0])
            ring.Append(',').Append(Corner(boundary[0]));

        return $$"""{"type":"Polygon","coordinates":[[{{ring}}]]}""";
    }

    private static string Corner(BoundaryPoint point) =>
        FormattableString.Invariant($"[{point.Longitude},{point.Latitude}]");

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
