namespace vos.Service.Phyllotaxis.Placement;

public sealed record WastePlace(Point Centre, double FromVillageCentreMetres, double BearingDegrees);

// Downwind of the homes, at least the buffer beyond the built area and clear of the greenhouses.
public static class WastePlant
{
    public static WastePlace? Place(
        Point village,
        double builtRadiusMetres,
        double greenhouseRadiusMetres,
        double bufferMetres,
        double windFromDegrees,
        Border border,
        WastePlantAssumptions assumptions)
    {
        throw new NotImplementedException();
    }
}
