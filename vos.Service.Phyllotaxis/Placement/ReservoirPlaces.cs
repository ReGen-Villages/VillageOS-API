namespace vos.Service.Phyllotaxis.Placement;

public sealed record ScoredCell(Cell Cell, double Score);

public sealed record ReservoirPlace(Point Centre, double RadiusMetres, Cell Cell, double HeightMetres, double SlopePercent, double DrainageCells);

// The lowest gentle ground that the site's water drains to.
public static class ReservoirPlaces
{
    public static IReadOnlyList<double> Scores(TerrainGrid grid, ReservoirAssumptions assumptions)
    {
        throw new NotImplementedException();
    }

    public static IReadOnlyList<ScoredCell> BestCells(TerrainGrid grid, IReadOnlyList<double> scores, ReservoirAssumptions assumptions)
    {
        throw new NotImplementedException();
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
        throw new NotImplementedException();
    }
}
