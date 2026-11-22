namespace vos.Service.Phyllotaxis.Placement;

public sealed record SolarField(Cell Cell, Point Centre, double RadiusMetres, double SunShare, double ShadeShare, double SlopePercent);

// Fields of solar panels over the crops: the sunniest gentle patches, apart from each other and clear of what is already placed.
public static class SolarFields
{
    public static IReadOnlyList<SolarField> BestPatches(
        TerrainGrid grid,
        SunAndShade sun,
        int count,
        int windowCells,
        double spacingMetres,
        IReadOnlyList<KeepClear> keepClear,
        SolarFieldAssumptions assumptions)
    {
        throw new NotImplementedException();
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
        throw new NotImplementedException();
    }
}
