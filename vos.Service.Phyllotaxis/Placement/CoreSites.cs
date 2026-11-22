namespace vos.Service.Phyllotaxis.Placement;

public sealed record ReservoirCell(Cell Cell, double RadiusMetres);

public sealed record CoreSite(
    Cell Cell,
    double Score,
    double HeightMetres,
    double MeanSlopePercent,
    double SteepShare,
    bool OnUnstableGround,
    bool InKeptClearGround,
    bool Accessible,
    double FlowPath,
    double AboveReservoirMetres,
    double FromMiddleMetres);

// Flat ground for the whole built area, off the flow paths, above the reservoir and near the middle of the site.
public static class CoreSites
{
    public static CoreSite? Best(
        TerrainGrid grid,
        double builtRadiusMetres,
        double coreRadiusMetres,
        double siteRadiusMetres,
        ReservoirCell? reservoir,
        CoreAssumptions assumptions)
    {
        throw new NotImplementedException();
    }
}
