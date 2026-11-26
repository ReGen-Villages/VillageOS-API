namespace vos.Service.Watershed.Terrain;

public sealed record SlopeClassBand(Guid Term, double? AtLeastPercent, double? BelowPercent);

// A sector with no centre is flat ground.
public sealed record AspectSector(Guid Term, double? CentreDegrees);

public sealed record ShareOfTheSite(Guid Term, double SharePercent, double AreaHectares);

public static class GroundShares
{
    public static IReadOnlyList<ShareOfTheSite> InSlopeClasses(TerrainAnalysisResult analysed, IReadOnlyList<SlopeClassBand> bands) =>
        throw new NotImplementedException();

    public static IReadOnlyList<ShareOfTheSite> FacingAspects(TerrainAnalysisResult analysed, IReadOnlyList<AspectSector> sectors) =>
        throw new NotImplementedException();
}
