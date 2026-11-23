namespace vos.Service.Phyllotaxis.Placement;

public sealed record SolarField(Cell Cell, Point Centre, double RadiusMetres, double SunShare, double ShadeShare, double SlopePercent);

public static class SolarFields
{
    public static IReadOnlyList<SolarField> BestPatches(
        TerrainGrid grid,
        SunAndShade sun,
        int count,
        int windowCells,
        double fieldRadiusMetres,
        IReadOnlyList<KeepClear> keepClear,
        SolarFieldAssumptions assumptions)
    {
        var spacingMetres = 2 * fieldRadiusMetres + assumptions.FieldGapMetres;
        var candidates = new List<SolarField>();
        for (var index = 0; index < grid.CellCount; index++)
        {
            if (!grid.Inside[index]) continue;
            var cell = grid.CellAt(index);
            if (WindowAround(grid, sun, cell, windowCells) is not { } window || window.SlopePercent > assumptions.PatchSteepestSlopePercent) continue;
            candidates.Add(new SolarField(cell, grid.CentreOf(cell), fieldRadiusMetres, window.SunShare, window.ShadeShare, window.SlopePercent));
        }

        var picked = new List<SolarField>();
        foreach (var candidate in candidates.OrderByDescending(candidate => candidate.SunShare))
        {
            if (picked.Count >= count) break;
            if (keepClear.Any(circle => circle.Centre.DistanceTo(candidate.Centre) < circle.RadiusMetres)) continue;
            if (grid.Ground[grid.IndexOf(candidate.Cell)] != Ground.Buildable) continue;
            if (picked.All(other => other.Centre.DistanceTo(candidate.Centre) >= spacingMetres))
                picked.Add(candidate);
        }

        return picked;
    }

    public static IReadOnlyList<SolarField> Place(
        TerrainGrid grid,
        SunAndShade sun,
        double agrivoltaicSquareMetres,
        Cell? village,
        double builtRadiusMetres,
        IReadOnlyList<ReservoirPlace> reservoirs,
        IReadOnlyList<TurbinePlace> turbines,
        ClearanceAssumptions clearance,
        SolarFieldAssumptions assumptions)
    {
        if (agrivoltaicSquareMetres <= 0) return [];

        var oneField = Math.PI * assumptions.FieldRadiusMetres * assumptions.FieldRadiusMetres;
        var mostFields = Math.Min(assumptions.FieldsMost, Math.Max(1, (int)Math.Ceiling(agrivoltaicSquareMetres / oneField)));

        for (var fields = mostFields; fields >= 1; fields--)
        {
            var radius = CircleRadius.OfArea(agrivoltaicSquareMetres / fields);
            var windowCells = Math.Max(0, Math.Min(assumptions.WindowMostCells, (int)Math.Floor(radius / grid.CellMetres)));

            foreach (var factor in assumptions.VillageClearanceFactors)
            {
                var keepClear = KeptClear(grid, village, builtRadiusMetres * factor, reservoirs, turbines, radius, clearance, assumptions);
                var placed = BestPatches(grid, sun, fields, windowCells, radius, keepClear, assumptions);
                if (placed.Count == fields) return placed;
            }
        }

        return [];
    }

    private sealed record Window(double SunShare, double ShadeShare, double SlopePercent);

    private static Window? WindowAround(TerrainGrid grid, SunAndShade sun, Cell centre, int windowCells)
    {
        double sunSum = 0, shadeSum = 0, slopeSum = 0;
        var cells = 0;
        for (var row = -windowCells; row <= windowCells; row++)
            for (var column = -windowCells; column <= windowCells; column++)
            {
                if (column * column + row * row > windowCells * windowCells) continue;
                var cell = new Cell(centre.Column + column, centre.Row + row);
                if (!grid.IsInside(cell)) return null;

                var index = grid.IndexOf(cell);
                sunSum += sun.SunShare[index];
                shadeSum += sun.ShadeShare[index];
                slopeSum += grid.SlopePercent[index];
                cells++;
            }

        return new Window(sunSum / cells, shadeSum / cells, slopeSum / cells);
    }

    private static List<KeepClear> KeptClear(
        TerrainGrid grid,
        Cell? village,
        double villageRadiusMetres,
        IReadOnlyList<ReservoirPlace> reservoirs,
        IReadOnlyList<TurbinePlace> turbines,
        double fieldRadiusMetres,
        ClearanceAssumptions clearance,
        SolarFieldAssumptions assumptions)
    {
        var keepClear = TurbinePlaces.AroundVillageAndReservoirs(grid, village, villageRadiusMetres, reservoirs, clearance)
            .Select(circle => circle with { RadiusMetres = circle.RadiusMetres + fieldRadiusMetres })
            .ToList();
        keepClear.AddRange(turbines.Select(turbine => new KeepClear(grid.CentreOf(turbine.Cell), assumptions.ClearOfTurbineMetres + fieldRadiusMetres)));
        return keepClear;
    }
}
