namespace vos.Service.Watershed.Terrain;

public sealed record SlopeClassBand(Guid Term, double? AtLeastPercent, double? BelowPercent)
{
    public bool Holds(double slopePercent) =>
        slopePercent >= (AtLeastPercent ?? double.NegativeInfinity) && slopePercent < (BelowPercent ?? double.PositiveInfinity);
}

public sealed record AspectSector(Guid Term, double? CentreDegrees);

public sealed record ShareOfTheSite(Guid Term, double SharePercent, double AreaHectares);

public static class GroundShares
{
    public static IReadOnlyList<ShareOfTheSite> InSlopeClasses(TerrainAnalysisResult analysed, IReadOnlyList<SlopeClassBand> bands) =>
        Shares(analysed, bands.Select(band => band.Term).ToArray(),
            cell => bands.Select((band, index) => (band, index)).FirstOrDefault(each => each.band.Holds(analysed.SlopePercent[cell]), (null!, -1)).index);

    public static IReadOnlyList<ShareOfTheSite> FacingAspects(TerrainAnalysisResult analysed, IReadOnlyList<AspectSector> sectors)
    {
        var flat = sectors.Select((sector, index) => (sector, index)).FirstOrDefault(each => each.sector.CentreDegrees is null, (null!, -1)).index;
        var facing = sectors.Select((sector, index) => (Centre: sector.CentreDegrees, Index: index))
            .Where(each => each.Centre is not null).Select(each => (Centre: each.Centre!.Value, each.Index)).ToArray();

        return Shares(analysed, sectors.Select(sector => sector.Term).ToArray(), cell =>
        {
            var aspect = analysed.AspectDegrees[cell];
            return double.IsNaN(aspect) ? flat : NearestClockwiseOnATie(facing, aspect);
        });
    }

    private static int NearestClockwiseOnATie((double Centre, int Index)[] facing, double aspect)
    {
        if (facing.Length == 0) return -1;
        double Away(double centre) => (aspect - centre + 540) % 360 - 180;
        return facing
            .OrderBy(each => Math.Abs(Away(each.Centre)))
            .ThenBy(each => Away(each.Centre) <= 0 ? 0 : 1)
            .First().Index;
    }

    private static IReadOnlyList<ShareOfTheSite> Shares(TerrainAnalysisResult analysed, Guid[] terms, Func<int, int> termOf)
    {
        var counts = new int[terms.Length];
        foreach (var cell in analysed.CellsCounted)
            if (termOf(cell) is var index and >= 0)
                counts[index]++;
        var total = analysed.CellsCounted.Count;
        return [.. terms.Select((term, index) => new ShareOfTheSite(term, 100.0 * counts[index] / total, counts[index] * analysed.CellHectares))];
    }
}
