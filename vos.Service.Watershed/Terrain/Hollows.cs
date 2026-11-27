namespace vos.Service.Watershed.Terrain;

public sealed record HollowRidge(int Basin, int OtherBasin, double Height);

public sealed record Hollow(
    IReadOnlyList<int> Cells, IReadOnlyList<int> BasinOfCell, int Basins, IReadOnlyList<HollowRidge> Ridges,
    int PourPoint, IReadOnlyList<int> Exits, double SpillLevel);

public sealed record HollowFill(IReadOnlyList<double> StandingDepths, double Overflow);

public static class Hollows
{
    public static IReadOnlyList<Hollow> Of(TerrainSurface surface, TerrainAnalysisResult analysed) => [];
}

public static class HollowWater
{
    public static HollowFill Fill(
        IReadOnlyList<double> heights, IReadOnlyList<int> basinOfCell, IReadOnlyList<HollowRidge> ridges,
        IReadOnlyList<double> inflowOfBasin, double spillLevel, double cellArea) =>
        new([.. heights.Select(_ => 0.0)], inflowOfBasin.Sum());
}
