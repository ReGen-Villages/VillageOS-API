using vos.Service.Phyllotaxis.Placement;

namespace vos.Service.Phyllotaxis.Tests;

internal static class Grids
{
    public static TerrainGrid Build(
        int columns,
        int rows,
        double cellMetres,
        Func<int, int, double> height,
        Func<int, int, double>? slope = null,
        Func<int, int, double>? drainage = null,
        Func<int, int, bool>? inside = null,
        Func<int, int, Ground>? ground = null)
    {
        var cells = Enumerable.Range(0, columns * rows).Select(index => (Column: index % columns, Row: index / columns)).ToList();
        return new TerrainGrid(
            columns,
            rows,
            cellMetres,
            cells.Select(cell => height(cell.Column, cell.Row)).ToList(),
            cells.Select(cell => slope?.Invoke(cell.Column, cell.Row) ?? 0).ToList(),
            cells.Select(cell => drainage?.Invoke(cell.Column, cell.Row) ?? 1).ToList(),
            cells.Select(cell => inside?.Invoke(cell.Column, cell.Row) ?? true).ToList(),
            cells.Select(cell => ground?.Invoke(cell.Column, cell.Row) ?? Ground.Buildable).ToList());
    }

    public static Border Rectangle(double west, double south, double east, double north) =>
        new([new Point(west, south), new Point(east, south), new Point(east, north), new Point(west, north)]);
}

// The demo's values, which the model holds as assumptions on the study.
internal static class DemoAssumptions
{
    public static readonly ReservoirAssumptions Reservoir = new(
        CatchmentWeight: 0.25,
        LownessWeight: 0.75,
        FullScoreSlopePercent: 5,
        NoScoreSlopePercent: 10,
        OnBorderFactor: 0.8,
        LeastReliefMetres: 1,
        CandidatesRead: 60,
        CandidateSpacingMetres: 150,
        CandidateSpacingCells: 3,
        GapMetres: 50,
        ClearOfVillageMetres: 30,
        PlacesShown: 3,
        PullInStepShare: 0.05,
        PullInLeastShare: 0.2,
        ScanLeastStepMetres: 40);

    public static readonly CoreAssumptions Core = new(
        WalkingSlopePercent: 5,
        RampSlopePercent: 8.33,
        SteepGroundShare: 0.1,
        FlatnessSlopePercent: 3,
        OnFlowPathPenalty: 0.6,
        BelowReservoirMetres: 10,
        BelowReservoirLeastFactor: 0.4,
        FromMiddlePenalty: 0.5,
        OnReservoirFactor: 0.3,
        InaccessibleFactor: 0.001,
        FitExponent: 2);

    public static readonly ClearanceAssumptions Clearance = new(ClearOfVillageMetres: 60, ClearOfReservoirMetres: 60);

    public static readonly TurbineAssumptions Turbine = new(
        SteepestSlopePercent: 15,
        SpacingRotorDiameters: 4,
        SmallestSpacingMetres: 200,
        PlacesLeast: 3,
        PlacesMost: 10);

    public static readonly SunAssumptions Sun = new(FirstHour: 7, LastHour: 17, LowestElevationSine: 0.08, ShadeSearchCells: 80);

    public static readonly SolarFieldAssumptions SolarField = new(
        PatchSteepestSlopePercent: 8,
        ClearOfTurbineMetres: 150,
        FieldRadiusMetres: 110,
        FieldsMost: 12,
        VillageClearanceFactors: [1.3, 1, 0.7],
        FieldGapMetres: 40,
        WindowMostCells: 8);

    public static readonly WastePlantAssumptions WastePlant = new(BearingFromDownwindDegrees: -12, ClearOfGreenhousesMetres: 30);
}
