namespace vos.Service.Intake;

// What one source may ask of the basemap route before it is made to wait. A screen of map is some forty
// tiles and every pan or zoom asks for about as many again, so the budget is a few minutes of somebody
// looking around their land, with room for an office sharing one address. It is counted apart from the
// submission budget, which a single screen of tiles would otherwise spend.
public static class TileRate
{
    public const string PolicyName = "public-tiles";

    public const int RequestsAllowed = 600;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
}
