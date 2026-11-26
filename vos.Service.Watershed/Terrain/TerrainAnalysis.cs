namespace vos.Service.Watershed.Terrain;

public sealed record TerrainSurface(int Columns, int Rows, double CellSizeMetres, IReadOnlyList<double> Heights, IReadOnlyList<bool> Inside);

public sealed record TerrainFigures(
    double AreaHectares, double LowestMetres, double HighestMetres, double MeanMetres,
    double SlopeMeanPercent, double SlopeMedianPercent, double SlopeNinetiethPercentilePercent,
    double HollowHectares, int LowestCell);

// An aspect of NaN is flat ground, which faces no direction.
public sealed record TerrainAnalysisResult(
    IReadOnlyList<double> SlopePercent, IReadOnlyList<double> AspectDegrees, IReadOnlyList<double> DrainageAreaHectares,
    IReadOnlyList<double> HollowDepthMetres, IReadOnlyList<int> CellsCounted, double CellHectares, TerrainFigures Figures);

public static class TerrainAnalysis
{
    public const double FlatBelowPercent = 1;
    public const double HollowAtLeastMetres = 0.5;
    public const double RiseAcrossAFilledFlatMetres = 1e-3;

    public static TerrainAnalysisResult Of(TerrainSurface surface) => throw new NotImplementedException();
}
