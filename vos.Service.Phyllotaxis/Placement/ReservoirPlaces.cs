namespace vos.Service.Phyllotaxis.Placement;

public sealed record ScoredCell(Cell Cell, double Score);

public sealed record ReservoirPlace(Point Centre, double RadiusMetres, Cell Cell, double HeightMetres, double SlopePercent, double DrainageCells);

public static class ReservoirPlaces
{
    public static IReadOnlyList<double> Scores(TerrainGrid grid, ReservoirAssumptions assumptions)
    {
        var scores = new double[grid.CellCount];
        var insideCells = Enumerable.Range(0, grid.CellCount).Where(index => grid.Inside[index]).ToList();
        if (insideCells.Count == 0) return scores;

        var largestDrainage = grid.LargestDrainageInside();
        var lowest = insideCells.Min(index => grid.HeightMetres[index]);
        var relief = insideCells.Max(index => grid.HeightMetres[index]) - lowest;

        foreach (var index in insideCells)
        {
            if (grid.Ground[index] != Ground.Buildable) continue;

            var catchment = TerrainGrid.DrainageShare(grid.DrainageCells[index], largestDrainage);
            var lowness = relief > assumptions.LeastReliefMetres ? 1 - (grid.HeightMetres[index] - lowest) / relief : 1;
            var gentleness = Gentleness(grid.SlopePercent[index], assumptions);
            var onBorder = IsOnBorder(grid, grid.CellAt(index)) ? assumptions.OnBorderFactor : 1;
            scores[index] = (assumptions.CatchmentWeight * catchment + assumptions.LownessWeight * lowness) * gentleness * onBorder;
        }

        return scores;
    }

    public static IReadOnlyList<ScoredCell> BestCells(TerrainGrid grid, IReadOnlyList<double> scores, ReservoirAssumptions assumptions)
    {
        var spacing = Math.Max(assumptions.CandidateSpacingMetres, assumptions.CandidateSpacingCells * grid.CellMetres);
        var best = new List<ScoredCell>();
        var ranked = Enumerable.Range(0, grid.CellCount)
            .Where(index => scores[index] > 0)
            .OrderByDescending(index => scores[index]);

        foreach (var index in ranked)
        {
            var cell = grid.CellAt(index);
            if (best.All(kept => grid.CentreOf(kept.Cell).DistanceTo(grid.CentreOf(cell)) >= spacing))
                best.Add(new ScoredCell(cell, scores[index]));
            if (best.Count >= assumptions.CandidatesRead) break;
        }

        return best;
    }

    public static IReadOnlyList<ReservoirPlace> Pick(
        TerrainGrid grid,
        IReadOnlyList<ScoredCell> bestCells,
        Border border,
        Point siteCentre,
        Point? village,
        double builtRadiusMetres,
        double reservoirRadiusMetres,
        ReservoirAssumptions assumptions)
    {
        var separation = 2 * reservoirRadiusMetres + assumptions.GapMetres;
        var picked = new List<Point>();

        bool ClearOfVillage(Point centre) =>
            village is not { } villageCentre || villageCentre.DistanceTo(centre) > builtRadiusMetres + reservoirRadiusMetres + assumptions.ClearOfVillageMetres;

        bool ApartFromThePicked(Point centre) => picked.All(other => other.DistanceTo(centre) >= separation);

        foreach (var candidate in bestCells)
        {
            if (picked.Count >= assumptions.PlacesShown) break;
            var moved = PulledInUntilItFits(grid.CentreOf(candidate.Cell), siteCentre, border, reservoirRadiusMetres, assumptions);
            if (moved is { } centre && ClearOfVillage(centre) && ApartFromThePicked(centre))
                picked.Add(centre);
        }

        if (picked.Count < assumptions.PlacesShown)
        {
            var lowestFirst = LatticeOverTheGrid(grid, siteCentre, Math.Max(assumptions.ScanLeastStepMetres, reservoirRadiusMetres / 2))
                .Where(centre => ClearOfVillage(centre) && border.HoldsCircle(centre, reservoirRadiusMetres) && IsBuildableInside(grid, centre))
                .OrderBy(centre => grid.HeightMetres[grid.IndexOf(grid.NearestCell(centre))]);

            foreach (var centre in lowestFirst)
            {
                if (picked.Count >= assumptions.PlacesShown) break;
                if (ApartFromThePicked(centre)) picked.Add(centre);
            }
        }

        return picked.Select(centre => PlaceAt(grid, centre, reservoirRadiusMetres)).ToList();
    }

    private static double Gentleness(double slopePercent, ReservoirAssumptions assumptions)
    {
        if (slopePercent <= assumptions.FullScoreSlopePercent) return 1;
        if (slopePercent >= assumptions.NoScoreSlopePercent) return 0;
        return (assumptions.NoScoreSlopePercent - slopePercent) / (assumptions.NoScoreSlopePercent - assumptions.FullScoreSlopePercent);
    }

    private static bool IsOnBorder(TerrainGrid grid, Cell cell) =>
        !grid.IsInside(cell with { Column = cell.Column - 1 })
        || !grid.IsInside(cell with { Column = cell.Column + 1 })
        || !grid.IsInside(cell with { Row = cell.Row - 1 })
        || !grid.IsInside(cell with { Row = cell.Row + 1 });

    private static Point? PulledInUntilItFits(Point centre, Point siteCentre, Border border, double radiusMetres, ReservoirAssumptions assumptions)
    {
        var lastStep = (int)Math.Round((1 - assumptions.PullInLeastShare) / assumptions.PullInStepShare);
        for (var step = 0; step <= lastStep; step++)
        {
            var share = 1 - step * assumptions.PullInStepShare;
            var moved = new Point(siteCentre.East + (centre.East - siteCentre.East) * share, siteCentre.North + (centre.North - siteCentre.North) * share);
            if (border.HoldsCircle(moved, radiusMetres)) return moved;
        }

        return null;
    }

    private static IEnumerable<Point> LatticeOverTheGrid(TerrainGrid grid, Point siteCentre, double stepMetres)
    {
        var east = (grid.Columns - 1) * grid.CellMetres;
        var north = (grid.Rows - 1) * grid.CellMetres;
        for (var column = (int)Math.Ceiling(-siteCentre.East / stepMetres); siteCentre.East + column * stepMetres <= east; column++)
            for (var row = (int)Math.Ceiling(-siteCentre.North / stepMetres); siteCentre.North + row * stepMetres <= north; row++)
                yield return new Point(siteCentre.East + column * stepMetres, siteCentre.North + row * stepMetres);
    }

    private static bool IsBuildableInside(TerrainGrid grid, Point centre)
    {
        var index = grid.IndexOf(grid.NearestCell(centre));
        return grid.Inside[index] && grid.Ground[index] == Ground.Buildable;
    }

    private static ReservoirPlace PlaceAt(TerrainGrid grid, Point centre, double radiusMetres)
    {
        var cell = grid.NearestCell(centre);
        var index = grid.IndexOf(cell);
        return new ReservoirPlace(centre, radiusMetres, cell, grid.HeightMetres[index], grid.SlopePercent[index], grid.DrainageCells[index]);
    }
}
