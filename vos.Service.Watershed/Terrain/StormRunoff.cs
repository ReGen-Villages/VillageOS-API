namespace vos.Service.Watershed.Terrain;

public sealed record RunoffInputs(
    double OpenGroundCoefficient, double RoofAndPavingCoefficient, double PlantedGroundCoefficient, double ImperviousShareOfBuiltGround,
    double SheetFlowManningRoughness, double ChannelManningRoughness, double ShortestTimeOfConcentrationMinutes,
    double SheetFlowLengthMetres, double SmallestOverlandSlope, double SmallestChannelSlope);

public sealed record DesignStormRain(double RunoffCoefficientMultiplier, RainfallIntensities Rain);

// The rational method of the Dubai Municipality Stormwater Design Guidelines (version 02, July 2024, section 4.4).
public static class StormRunoff
{
    // The kinematic wave equation for sheet flow in metric units, with rain in millimetres an hour.
    private const double KinematicWaveCoefficient = 6.943;

    // Shallow concentrated flow over unpaved ground: V = 0.305 √(100 S) metres a second.
    private const double ShallowFlowCoefficient = 0.305;

    // Lacey's regime width of an alluvial channel: P = 4.75 √Q.
    private const double RegimeWidthCoefficient = 4.75;

    private const int TimeOfConcentrationRounds = 6;
    private const double SecondsAnHourByMillimetresAMetre = 3.6e6;

    public static IReadOnlyList<double> GridCodesOf(
        TerrainSurface surface, TerrainAnalysisResult analysed, IReadOnlyList<Hollow> hollows, RunoffInputs inputs, DesignStormRain storm,
        bool builtOver, IReadOnlyList<RunoffClassRule> classes)
    {
        var (columns, cell) = (surface.Columns, surface.CellSizeMetres);
        var count = surface.Heights.Count;
        var cellArea = cell * cell;
        var filled = analysed.FilledHeights;
        var builtCoefficient = inputs.ImperviousShareOfBuiltGround * inputs.RoofAndPavingCoefficient
                               + (1 - inputs.ImperviousShareOfBuiltGround) * inputs.PlantedGroundCoefficient;

        var area = Enumerable.Repeat(cellArea, count).ToArray();
        var runoffArea = new double[count];
        var flowLength = new double[count];
        var topHeight = new double[count];
        for (var index = 0; index < count; index++)
        {
            var built = builtOver && surface.Inside[index] ? 1 : 0;
            runoffArea[index] = cellArea * (built * builtCoefficient + (1 - built) * inputs.OpenGroundCoefficient);
            topHeight[index] = filled[index];
        }
        foreach (var index in Enumerable.Range(0, count).OrderByDescending(index => filled[index]))
        {
            var next = analysed.DrainsTo[index];
            if (next < 0) continue;
            area[next] += area[index];
            runoffArea[next] += runoffArea[index];
            var reach = flowLength[index] + Math.Sqrt(Math.Pow(index % columns - next % columns, 2) + Math.Pow(index / columns - next / columns, 2)) * cell;
            if (reach <= flowLength[next]) continue;
            flowLength[next] = reach;
            topHeight[next] = topHeight[index];
        }

        double Coefficient(int index) => Math.Min(1, runoffArea[index] / area[index] * storm.RunoffCoefficientMultiplier);

        double TimeOfConcentrationMinutes(int index)
        {
            var length = Math.Max(flowLength[index], cell / 2);
            var slope = Math.Max(inputs.SmallestOverlandSlope, (topHeight[index] - filled[index]) / Math.Max(length, cell));
            var sheet = Math.Min(length, inputs.SheetFlowLengthMetres);
            var shallowMinutes = (length - sheet) / (60 * ShallowFlowCoefficient * Math.Sqrt(slope * 100));
            var minutes = inputs.ShortestTimeOfConcentrationMinutes;
            for (var round = 0; round < TimeOfConcentrationRounds; round++)
            {
                var rain = storm.Rain.MillimetresPerHourOver(Math.Max(inputs.ShortestTimeOfConcentrationMinutes, minutes));
                minutes = Math.Max(inputs.ShortestTimeOfConcentrationMinutes,
                    KinematicWaveCoefficient * Math.Pow(inputs.SheetFlowManningRoughness * sheet, 0.6) / (Math.Pow(rain, 0.4) * Math.Pow(slope, 0.3))
                    + shallowMinutes);
            }
            return minutes;
        }

        var inside = Enumerable.Range(0, count).Where(index => surface.Inside[index]).ToArray();
        var minutesOf = new double[count];
        var flow = new double[count];
        foreach (var index in inside)
        {
            minutesOf[index] = TimeOfConcentrationMinutes(index);
            flow[index] = Coefficient(index) * storm.Rain.MillimetresPerHourOver(minutesOf[index]) * area[index] / SecondsAnHourByMillimetresAMetre;
        }
        var largest = inside.Length == 0 ? 0 : inside.Max(index => flow[index]);

        double RunoffVolume(int index)
        {
            var minutes = TimeOfConcentrationMinutes(index);
            return Coefficient(index) * storm.Rain.MillimetresPerHourOver(minutes) * (minutes / 60) / 1000 * area[index];
        }
        var standing = StandingDepths(surface, analysed, hollows, area, RunoffVolume);

        var codes = Enumerable.Repeat(double.NaN, count).ToArray();
        foreach (var index in inside)
        {
            var hollow = analysed.HollowDepthMetres[index];
            var (depth, depthTimesVelocity) = (0.0, 0.0);
            if (flow[index] > 0)
            {
                var width = Math.Min(cell, RegimeWidthCoefficient * Math.Sqrt(flow[index]));
                var channelSlope = Math.Max(inputs.SmallestChannelSlope, analysed.SlopePercent[index] / 100);
                depth = Math.Pow(flow[index] * inputs.ChannelManningRoughness / (width * Math.Sqrt(channelSlope)), 0.6);
                depthTimesVelocity = flow[index] / width;
            }
            var measures = new RunoffCellMeasures(
                hollow, standing[index], largest > 0 ? flow[index] / largest : 0, area[index] / cellArea, depth, depthTimesVelocity);
            if (RunoffClassRules.ClassOf(classes, measures) is { } runoffClass) codes[index] = runoffClass.GridCode;
        }
        return codes;
    }

    // Water gathers at the bottom of a hollow, on purpose unlike a method that counts only the runoff along each cell's own route.
    private static double[] StandingDepths(
        TerrainSurface surface, TerrainAnalysisResult analysed, IReadOnlyList<Hollow> hollowsOfTheTerrain, double[] area, Func<int, double> runoffVolume)
    {
        var cellArea = surface.CellSizeMetres * surface.CellSizeMetres;
        var standing = new double[surface.Heights.Count];
        var hollows = hollowsOfTheTerrain.OrderByDescending(hollow => hollow.SpillLevel).ToList();
        var hollowOf = new Dictionary<int, int>();
        for (var number = 0; number < hollows.Count; number++)
            foreach (var cell in hollows[number].Cells) hollowOf[cell] = number;
        var entering = Hollows.AreaEnteringEachBasin(hollows, analysed.DrainsTo, area, cellArea);
        var keptAbove = new double[hollows.Count];

        for (var number = 0; number < hollows.Count; number++)
        {
            var hollow = hollows[number];
            var gathered = Math.Max(0, hollow.Exits.Sum(runoffVolume) - keptAbove[number]);
            var fill = HollowWater.Fill(
                [.. hollow.Cells.Select(cell => surface.Heights[cell])], hollow.BasinOfCell, hollow.Ridges,
                HollowWater.InflowOfEachBasin(entering[number], gathered), hollow.SpillLevel, cellArea);
            for (var index = 0; index < hollow.Cells.Count; index++) standing[hollow.Cells[index]] = fill.StandingDepths[index];

            var kept = gathered - fill.Overflow + keptAbove[number];
            for (var cell = hollow.PourPoint; cell >= 0; cell = analysed.DrainsTo[cell])
                if (hollowOf.TryGetValue(cell, out var below) && below != number)
                {
                    keptAbove[below] += kept;
                    break;
                }
        }
        return standing;
    }
}
