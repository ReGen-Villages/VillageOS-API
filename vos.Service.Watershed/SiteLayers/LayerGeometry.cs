using System.Text.Json;
using vos.Service.Watershed.Terrain;

namespace vos.Service.Watershed.SiteLayers;

public sealed class LocalFrame
{
    private LocalFrame()
    {
    }

    public static LocalFrame Around(IReadOnlyList<Position> border) => new();

    public PlanePoint At(Position position) => default;
}

public abstract record LayerGeometry
{
    public static LayerGeometry? Read(JsonElement stated, LocalFrame frame) => null;
}

public sealed record LayerShape(PlaneShape Shape) : LayerGeometry;

public sealed record LayerLines(IReadOnlyList<PlaneLine> Lines) : LayerGeometry;
