namespace vos.Service.Watershed.Terrain;

public sealed record RunoffInputs(
    double OpenGroundCoefficient, double RoofAndPavingCoefficient, double PlantedGroundCoefficient, double ImperviousShareOfBuiltGround,
    double SheetFlowManningRoughness, double ChannelManningRoughness, double ShortestTimeOfConcentrationMinutes,
    double SheetFlowLengthMetres, double SmallestOverlandSlope, double SmallestChannelSlope);

public sealed record DesignStormRain(double RunoffCoefficientMultiplier, RainfallIntensities Rain);

public static class StormRunoff
{
    public static IReadOnlyList<double> GridCodesOf(
        TerrainSurface surface, TerrainAnalysisResult analysed, RunoffInputs inputs, DesignStormRain storm, bool builtOver,
        IReadOnlyList<RunoffClassRule> classes) => [.. Enumerable.Repeat(double.NaN, surface.Heights.Count)];
}
