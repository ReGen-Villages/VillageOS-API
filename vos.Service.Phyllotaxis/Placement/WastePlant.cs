namespace vos.Service.Phyllotaxis.Placement;

public sealed record WastePlace(Point Centre, double FromVillageCentreMetres, double BearingDegrees);

public static class WastePlant
{
    // Unlike the demo, the plant is never pulled back inside its buffer to fit the border: where it does not fit there is no place.
    public static WastePlace? Place(
        Point village,
        double builtRadiusMetres,
        double greenhouseRadiusMetres,
        double bufferMetres,
        double windFromDegrees,
        Border border,
        WastePlantAssumptions assumptions)
    {
        var distance = Math.Max(builtRadiusMetres + bufferMetres, greenhouseRadiusMetres + assumptions.ClearOfGreenhousesMetres);
        var bearing = ((windFromDegrees + 180 + assumptions.BearingFromDownwindDegrees) % 360 + 360) % 360;
        var radians = bearing * Math.PI / 180;
        var centre = new Point(village.East + distance * Math.Sin(radians), village.North + distance * Math.Cos(radians));

        return border.Holds(centre) ? new WastePlace(centre, distance, bearing) : null;
    }
}
