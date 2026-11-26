namespace vos.Service.Watershed.Terrain;

public sealed record RunoffCellMeasures(
    double HollowMetres, double StandingWaterMetres, double ShareOfLargestFlow, double CatchmentCells,
    double FlowDepthMetres, double DepthTimesVelocity);

public sealed record RunoffClassRule(
    long Order, long GridCode, IReadOnlyDictionary<string, double> Conditions, IReadOnlyList<IReadOnlyDictionary<string, double>> Alternatives);

public static class RunoffClassRules
{
    public static readonly IReadOnlyDictionary<string, Func<RunoffCellMeasures, double, bool>> Known =
        new Dictionary<string, Func<RunoffCellMeasures, double, bool>>
        {
            ["hollowAtLeastMetres"] = (cell, least) => cell.HollowMetres >= least,
            ["standingWaterAtLeastMetres"] = (cell, least) => cell.StandingWaterMetres >= least,
            ["shareOfLargestFlowAtLeast"] = (cell, least) => cell.ShareOfLargestFlow >= least,
            ["catchmentAboveCells"] = (cell, above) => cell.CatchmentCells > above,
            ["flowDepthAboveMetres"] = (cell, above) => cell.FlowDepthMetres > above,
            ["depthTimesVelocityAboveSquareMetresPerSecond"] = (cell, above) => cell.DepthTimesVelocity > above,
        };

    public static RunoffClassRule? ClassOf(IReadOnlyList<RunoffClassRule> classes, RunoffCellMeasures cell) =>
        classes.OrderByDescending(runoffClass => runoffClass.Order).FirstOrDefault(runoffClass =>
            AllHold(runoffClass.Conditions, cell)
            && (runoffClass.Alternatives.Count == 0 || runoffClass.Alternatives.Any(alternative => AllHold(alternative, cell))));

    private static bool AllHold(IReadOnlyDictionary<string, double> conditions, RunoffCellMeasures cell) =>
        conditions.All(condition => Known.TryGetValue(condition.Key, out var holds) && holds(cell, condition.Value));
}
