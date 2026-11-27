namespace vos.Service.Watershed.Terrain;

public sealed record RunoffCellMeasures(
    double HollowMetres, double StandingWaterMetres, double ShareOfLargestFlow, double CatchmentCells,
    double FlowDepthMetres, double DepthTimesVelocity);

public sealed record RunoffClassRule(
    long Order, long GridCode, IReadOnlyDictionary<string, double> Conditions, IReadOnlyList<IReadOnlyDictionary<string, double>> Alternatives);

public static class RunoffClassRules
{
    public static readonly IReadOnlyDictionary<string, Func<RunoffCellMeasures, double, bool>> Known =
        new Dictionary<string, Func<RunoffCellMeasures, double, bool>>();

    public static RunoffClassRule? ClassOf(IReadOnlyList<RunoffClassRule> classes, RunoffCellMeasures cell) => null;
}
