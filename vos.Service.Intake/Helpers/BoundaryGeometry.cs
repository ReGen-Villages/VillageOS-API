using System.Text;
using vos.Service.Intake.Models;

namespace vos.Service.Intake.Helpers;

public static class BoundaryGeometry
{
    public static IReadOnlyList<(double Latitude, double Longitude)> Corners(IReadOnlyList<BoundaryPoint> boundary) =>
        [.. boundary.Select(corner => (corner.Latitude, corner.Longitude))];

    // The boundary as a GeoJSON polygon: longitude before latitude, and the first corner repeated
    // at the end to close the ring, as that format requires.
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
}
