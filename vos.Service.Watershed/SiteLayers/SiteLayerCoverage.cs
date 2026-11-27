namespace vos.Service.Watershed.SiteLayers;

public sealed record ShapeCoverage(double HectaresInside, double ShareOfSitePercent, double DistanceFromSiteMetres);

public sealed record LineCoverage(double MetresInside, bool CrossesBorder, double DistanceFromSiteMetres);

public static class SiteLayerCoverage
{
    private const double SquareMetresInAHectare = 10000;

    public static ShapeCoverage OfShape(PlaneShape site, PlaneShape shape)
    {
        var inside = shape.OverlapSquareMetres(site);
        return new ShapeCoverage(
            inside / SquareMetresInAHectare, 100 * inside / site.AreaSquareMetres, inside > 0 ? 0 : shape.OutlineDistanceMetres(site));
    }

    public static LineCoverage OfLines(PlaneShape site, IReadOnlyList<PlaneLine> lines)
    {
        var inside = lines.Sum(site.LengthInsideMetres);
        return new LineCoverage(inside, lines.Any(site.IsCrossedBy), inside > 0 ? 0 : lines.Min(site.DistanceMetres));
    }
}
