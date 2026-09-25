namespace vos.Service.Intake;

// What one address may ask of the map tile route before it is made to wait. A screen of map is about
// forty tiles and each pan or zoom asks for about as many again, so this allows a few minutes of looking
// around a piece of land, with room for an office sharing one address. It is counted apart from the
// submission budget, which one screen of tiles would otherwise use up. It still has a limit because a
// tile not yet cached is a request to the imagery provider, from a route that needs no sign-in.
public static class TileRate
{
    public const string PolicyName = "public-tiles";

    public const int RequestsAllowed = 600;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
}
