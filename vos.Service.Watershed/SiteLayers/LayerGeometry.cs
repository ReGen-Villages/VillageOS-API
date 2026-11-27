using System.Text.Json;
using vos.Service.Watershed.Terrain;

namespace vos.Service.Watershed.SiteLayers;

public sealed class LocalFrame
{
    private readonly Position _middle;
    private readonly double _metresPerDegreeOfLongitude;

    private LocalFrame(Position middle)
    {
        _middle = middle;
        _metresPerDegreeOfLongitude = EarthDistances.MetresPerDegreeOfLongitudeAt(middle.Latitude);
    }

    public static LocalFrame Around(IReadOnlyList<Position> border) =>
        new(new Position(border.Average(corner => corner.Latitude), border.Average(corner => corner.Longitude)));

    public PlanePoint At(Position position) => new(
        (position.Longitude - _middle.Longitude) * _metresPerDegreeOfLongitude,
        (position.Latitude - _middle.Latitude) * EarthDistances.MetresPerDegreeOfLatitude);
}

public abstract record LayerGeometry
{
    public static LayerGeometry? Read(JsonElement stated, LocalFrame frame)
    {
        try
        {
            var geometry = stated.ValueKind == JsonValueKind.String ? JsonDocument.Parse(stated.GetString()!).RootElement : stated;
            if (geometry.ValueKind != JsonValueKind.Object
                || !geometry.TryGetProperty("type", out var type) || !geometry.TryGetProperty("coordinates", out var coordinates))
                return null;

            PlanePoint[] Points(JsonElement positions) =>
                [.. positions.EnumerateArray().Select(position => frame.At(new Position(position[1].GetDouble(), position[0].GetDouble())))];
            PlanePoint[] Ring(JsonElement positions)
            {
                var corners = Points(positions);
                return corners.Length > 1 && corners[0] == corners[^1] ? corners[..^1] : corners;
            }
            PlanePolygon? Polygon(JsonElement rings)
            {
                var corners = rings.EnumerateArray().Select(Ring).ToList();
                return corners.Count > 0 && corners.All(ring => ring.Length >= 3) ? new PlanePolygon(corners[0], [.. corners.Skip(1)]) : null;
            }
            PlaneLine? Line(JsonElement positions) => Points(positions) is { Length: >= 2 } points ? new PlaneLine(points) : null;

            switch (type.GetString())
            {
                case "Polygon":
                    return Polygon(coordinates) is { } polygon ? new LayerShape(new PlaneShape([polygon])) : null;
                case "MultiPolygon":
                    var polygons = coordinates.EnumerateArray().Select(Polygon).ToList();
                    return polygons.Count > 0 && !polygons.Contains(null) ? new LayerShape(new PlaneShape([.. polygons.OfType<PlanePolygon>()])) : null;
                case "LineString":
                    return Line(coordinates) is { } line ? new LayerLines([line]) : null;
                case "MultiLineString":
                    var lines = coordinates.EnumerateArray().Select(Line).ToList();
                    return lines.Count > 0 && !lines.Contains(null) ? new LayerLines([.. lines.OfType<PlaneLine>()]) : null;
                default:
                    return null;
            }
        }
        catch (Exception unreadable) when (unreadable is JsonException or InvalidOperationException or IndexOutOfRangeException or FormatException)
        {
            return null;
        }
    }
}

public sealed record LayerShape(PlaneShape Shape) : LayerGeometry;

public sealed record LayerLines(IReadOnlyList<PlaneLine> Lines) : LayerGeometry;
