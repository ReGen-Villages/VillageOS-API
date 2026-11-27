namespace vos.Service.Watershed.SiteLayers;

public readonly record struct PlanePoint(double X, double Y);

public sealed record PlanePolygon(IReadOnlyList<PlanePoint> Outer, IReadOnlyList<IReadOnlyList<PlanePoint>> Holes);

public sealed record PlaneLine(IReadOnlyList<PlanePoint> Points);

public sealed class PlaneShape
{
    public PlaneShape(IReadOnlyList<PlanePolygon> polygons)
    {
    }

    public double AreaSquareMetres => 0;

    public double OverlapSquareMetres(PlaneShape other) => 0;

    public double OutlineDistanceMetres(PlaneShape other) => 0;

    public double LengthInsideMetres(PlaneLine line) => 0;

    public bool IsCrossedBy(PlaneLine line) => false;

    public double DistanceMetres(PlaneLine line) => 0;
}
