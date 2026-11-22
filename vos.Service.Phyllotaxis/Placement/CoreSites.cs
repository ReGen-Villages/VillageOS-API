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
        var largestDrainage = grid.LargestDrainageInside();
        var builtCells = Math.Max(1, TerrainGrid.RoundHalfUp(builtRadiusMetres / grid.CellMetres));
        var coreCells = Math.Max(1, TerrainGrid.RoundHalfUp(coreRadiusMetres / grid.CellMetres));
        var middle = new Point((grid.Columns - 1) / 2.0 * grid.CellMetres, (grid.Rows - 1) / 2.0 * grid.CellMetres);
        double? reservoirHeight = reservoir is { } held ? grid.HeightMetres[grid.IndexOf(held.Cell)] : null;

        CoreSite? best = null;
        for (var index = 0; index < grid.CellCount; index++)
        {
            if (!grid.Inside[index]) continue;
            var cell = grid.CellAt(index);
            var underIt = Under(grid, cell, builtCells, coreCells, largestDrainage, assumptions);
            if (underIt.InsideCount == 0) continue;

            var centre = grid.CentreOf(cell);
            var meanSlope = underIt.SlopeSum / underIt.InsideCount;
            var steepShare = (double)underIt.SteepCount / underIt.InsideCount;
            var fits = (double)underIt.InsideCount / underIt.CellCount;
            var fromMiddle = centre.DistanceTo(middle);
            var aboveReservoir = reservoirHeight is { } height ? grid.HeightMetres[index] - height : 0;
            var accessible = meanSlope <= assumptions.WalkingSlopePercent && steepShare <= assumptions.SteepGroundShare && underIt.KeptClearCount == 0;

            var flatness = 1 / (1 + meanSlope / assumptions.FlatnessSlopePercent);
            var offFlowPaths = 1 - assumptions.OnFlowPathPenalty * underIt.FlowPath;
            var aboveTheReservoir = aboveReservoir >= 0
                ? 1
                : Math.Max(assumptions.BelowReservoirLeastFactor, 1 + aboveReservoir / assumptions.BelowReservoirMetres);
            var nearTheMiddle = 1 - assumptions.FromMiddlePenalty * Math.Min(1, fromMiddle / Math.Max(1, siteRadiusMetres));
            var offTheReservoir = reservoir is { } onIt && centre.DistanceTo(grid.CentreOf(onIt.Cell)) < onIt.RadiusMetres + coreRadiusMetres
                ? assumptions.OnReservoirFactor
                : 1;
            var reachable = accessible ? 1 : assumptions.InaccessibleFactor / (1 + underIt.KeptClearCount);
            var score = flatness * offFlowPaths * aboveTheReservoir * nearTheMiddle * offTheReservoir * Math.Pow(fits, assumptions.FitExponent) * reachable;

            if (best is null || score > best.Score)
                best = new CoreSite(
                    cell,
                    score,
                    grid.HeightMetres[index],
                    meanSlope,
                    steepShare,
                    OnUnstableGround: underIt.UnstableCount > 0,
                    InKeptClearGround: underIt.KeptClearCount > underIt.UnstableCount,
                    accessible,
                    underIt.FlowPath,
                    aboveReservoir,
                    fromMiddle);
        }

        return best;
    }

    private sealed record GroundUnder(int CellCount, int InsideCount, double SlopeSum, int SteepCount, int KeptClearCount, int UnstableCount, double FlowPath);

    private static GroundUnder Under(TerrainGrid grid, Cell centre, int builtCells, int coreCells, double largestDrainage, CoreAssumptions assumptions)
    {
        int cells = 0, inside = 0, steep = 0, keptClear = 0, unstable = 0;
        double slopeSum = 0, flowPath = 0;

        for (var row = -builtCells; row <= builtCells; row++)
            for (var column = -builtCells; column <= builtCells; column++)
            {
                if (column * column + row * row > builtCells * builtCells) continue;
                cells++;
                var cell = new Cell(centre.Column + column, centre.Row + row);
                if (!grid.IsInside(cell)) continue;

                var index = grid.IndexOf(cell);
                inside++;
                slopeSum += grid.SlopePercent[index];
                if (grid.SlopePercent[index] > assumptions.RampSlopePercent) steep++;
                if (grid.Ground[index] != Ground.Buildable)
                {
                    keptClear++;
                    if (grid.Ground[index] == Ground.Unstable) unstable++;
                }

                if (column * column + row * row <= coreCells * coreCells)
                    flowPath = Math.Max(flowPath, TerrainGrid.DrainageShare(grid.DrainageCells[index], largestDrainage));
            }

        return new GroundUnder(cells, inside, slopeSum, steep, keptClear, unstable, flowPath);
    }
}
