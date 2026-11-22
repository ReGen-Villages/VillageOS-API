namespace vos.Service.Phyllotaxis.Placement;

public sealed record KeepClear(Point Centre, double RadiusMetres);

public sealed record TurbinePlace(Cell Cell, Point Centre, double HeightMetres);

// The highest gentle buildable ground, spaced by rotor and clear of the village and the reservoirs.
public static class TurbinePlaces
{
    public static double SpacingMetres(double rotorDiameterMetres, TurbineAssumptions assumptions)
    {
        throw new NotImplementedException();
    }

    public static int PlacesWanted(int turbinesNeeded, TurbineAssumptions assumptions)
    {
        throw new NotImplementedException();
    }

    public static IReadOnlyList<KeepClear> AroundVillageAndReservoirs(
        TerrainGrid grid,
        Cell? village,
        double builtRadiusMetres,
        IReadOnlyList<ReservoirPlace> reservoirs,
        ClearanceAssumptions assumptions)
    {
        throw new NotImplementedException();
    }

    public static IReadOnlyList<TurbinePlace> Pick(
        TerrainGrid grid,
        int places,
        double spacingMetres,
        IReadOnlyList<KeepClear> keepClear,
        TurbineAssumptions assumptions)
    {
        throw new NotImplementedException();
    }
}
