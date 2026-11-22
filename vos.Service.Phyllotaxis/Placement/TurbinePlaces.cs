namespace vos.Service.Phyllotaxis.Placement;

public sealed record KeepClear(Point Centre, double RadiusMetres);

public sealed record TurbinePlace(Cell Cell, Point Centre, double HeightMetres);

public static class TurbinePlaces
{
    public static double SpacingMetres(double rotorDiameterMetres, TurbineAssumptions assumptions) =>
        Math.Max(assumptions.SmallestSpacingMetres, assumptions.SpacingRotorDiameters * rotorDiameterMetres);

    public static int PlacesWanted(int turbinesNeeded, TurbineAssumptions assumptions) =>
        Math.Min(assumptions.PlacesMost, Math.Max(assumptions.PlacesLeast, turbinesNeeded));

    public static IReadOnlyList<KeepClear> AroundVillageAndReservoirs(
        TerrainGrid grid,
        Cell? village,
        double builtRadiusMetres,
        IReadOnlyList<ReservoirPlace> reservoirs,
        ClearanceAssumptions assumptions)
    {
        KeepClear[] aroundVillage = village is { } core
            ? [new KeepClear(grid.CentreOf(core), builtRadiusMetres + assumptions.ClearOfVillageMetres)]
            : [];
        return aroundVillage
            .Concat(reservoirs.Select(reservoir => new KeepClear(grid.CentreOf(reservoir.Cell), reservoir.RadiusMetres + assumptions.ClearOfReservoirMetres)))
            .ToList();
    }

    public static IReadOnlyList<TurbinePlace> Pick(
        TerrainGrid grid,
        int places,
        double spacingMetres,
        IReadOnlyList<KeepClear> keepClear,
        TurbineAssumptions assumptions)
    {
        var picked = new List<TurbinePlace>();
        var highestFirst = Enumerable.Range(0, grid.CellCount)
            .Where(index => grid.Inside[index] && grid.Ground[index] == Ground.Buildable && grid.SlopePercent[index] < assumptions.SteepestSlopePercent)
            .OrderByDescending(index => grid.HeightMetres[index]);

        foreach (var index in highestFirst)
        {
            if (picked.Count >= places) break;
            var cell = grid.CellAt(index);
            var centre = grid.CentreOf(cell);
            if (keepClear.Any(circle => circle.Centre.DistanceTo(centre) < circle.RadiusMetres)) continue;
            if (picked.All(other => other.Centre.DistanceTo(centre) >= spacingMetres))
                picked.Add(new TurbinePlace(cell, centre, grid.HeightMetres[index]));
        }

        return picked;
    }
}
