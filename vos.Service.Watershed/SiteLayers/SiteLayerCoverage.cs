namespace vos.Service.Watershed.SiteLayers;

public sealed record ShapeCoverage(double HectaresInside, double ShareOfSitePercent, double DistanceFromSiteMetres);

public sealed record LineCoverage(double MetresInside, bool CrossesBorder, double DistanceFromSiteMetres);

public static class SiteLayerCoverage
{
    public static ShapeCoverage OfShape(PlaneShape site, PlaneShape shape) => new(0, 0, 0);

    public static LineCoverage OfLines(PlaneShape site, IReadOnlyList<PlaneLine> lines) => new(0, false, 0);
}
