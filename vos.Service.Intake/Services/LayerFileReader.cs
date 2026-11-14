using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using vos.Service.Intake.Models;

namespace vos.Service.Intake.Services;

public sealed record LayerShape(string Name, string Geometry, int Corners);

// Points and unnamed shapes are left out, since a planner chooses a kind for each shape by its name.
public static class LayerFileReader
{
    private static readonly XmlReaderSettings WithoutExternalReferences = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    public static IReadOnlyList<LayerShape> Read(string fileName, Stream bytes)
    {
        try
        {
            return Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".kml" => Kml(bytes),
                ".kmz" => Kml(FirstKmlInside(bytes)),
                ".geojson" or ".json" => GeoJson(bytes),
                ".gpx" => Gpx(bytes),
                _ => throw Unreadable("a layer file is KML, KMZ, GeoJSON or GPX"),
            };
        }
        catch (Exception error) when (error is XmlException or JsonException or InvalidDataException
                                          or FormatException or InvalidOperationException or KeyNotFoundException
                                          or IndexOutOfRangeException)
        {
            throw Unreadable(error.Message);
        }
    }

    private static List<LayerShape> Kml(Stream bytes)
    {
        var document = XDocument.Load(XmlReader.Create(bytes, WithoutExternalReferences));
        var shapes = new List<LayerShape>();
        foreach (var placemark in document.Descendants().Where(element => element.Name.LocalName == "Placemark"))
        {
            var parts = new List<Part>();
            foreach (var element in placemark.Descendants())
            {
                switch (element.Name.LocalName)
                {
                    case "Polygon":
                        parts.Add(Area.Of([
                            .. Children(element, "outerBoundaryIs").Concat(Children(element, "innerBoundaryIs"))
                                .SelectMany(boundary => Children(boundary, "LinearRing"))
                                .Select(ring => KmlPositions(ring))]));
                        break;
                    case "LineString":
                        parts.Add(Line.Of(KmlPositions(element)));
                        break;
                    case "LinearRing" when !element.Ancestors().Any(ancestor => ancestor.Name.LocalName == "Polygon"):
                        parts.Add(Area.Of([KmlPositions(element)]));
                        break;
                }
            }
            Add(shapes, Children(placemark, "name").FirstOrDefault()?.Value, parts);
        }
        return shapes;
    }

    // The archive is unpacked no further than a file may be, so a small archive cannot make the service hold more.
    private static MemoryStream FirstKmlInside(Stream bytes)
    {
        using var archive = new ZipArchive(bytes, ZipArchiveMode.Read, leaveOpen: true);
        var kml = archive.Entries.FirstOrDefault(entry => entry.FullName.EndsWith(".kml", StringComparison.OrdinalIgnoreCase))
                  ?? throw Unreadable("the archive holds no KML file");

        using var unpacking = kml.Open();
        var unpacked = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = unpacking.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (unpacked.Length + read > SharedDocumentService.MaximumFileBytes)
                throw new SubmissionError(RefusalCode.FileTooLarge,
                    $"The KML inside the archive is larger than the {SharedDocumentService.MaximumFileBytes / (1024 * 1024)} MB a file may be.",
                    Refusal.With(("megabytes", SharedDocumentService.MaximumFileBytes / (1024 * 1024))));
            unpacked.Write(buffer, 0, read);
        }
        unpacked.Position = 0;
        return unpacked;
    }

    private static List<Position> KmlPositions(XElement geometry)
    {
        var coordinates = Children(geometry, "coordinates").FirstOrDefault()
                          ?? throw Unreadable($"a {geometry.Name.LocalName} has no coordinates");
        return [.. coordinates.Value
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(tuple => tuple.Split(','))
            .Select(values => Position.At(Number(values[0]), Number(values[1])))];
    }

    private static List<LayerShape> Gpx(Stream bytes)
    {
        var document = XDocument.Load(XmlReader.Create(bytes, WithoutExternalReferences));
        var shapes = new List<LayerShape>();
        foreach (var track in document.Descendants().Where(element => element.Name.LocalName == "trk"))
            Add(shapes, Children(track, "name").FirstOrDefault()?.Value,
                [.. Children(track, "trkseg").Select(segment => Line.Of(GpxPositions(segment, "trkpt")))]);
        foreach (var route in document.Descendants().Where(element => element.Name.LocalName == "rte"))
            Add(shapes, Children(route, "name").FirstOrDefault()?.Value, [Line.Of(GpxPositions(route, "rtept"))]);
        return shapes;
    }

    private static List<Position> GpxPositions(XElement parent, string pointName) =>
    [
        .. Children(parent, pointName).Select(point => Position.At(
            Number(point.Attribute("lon")?.Value ?? throw Unreadable($"a {pointName} has no longitude")),
            Number(point.Attribute("lat")?.Value ?? throw Unreadable($"a {pointName} has no latitude")))),
    ];

    private static List<LayerShape> GeoJson(Stream bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        IEnumerable<JsonElement> features = root.GetProperty("type").GetString() switch
        {
            "FeatureCollection" => root.GetProperty("features").EnumerateArray(),
            "Feature" => [root],
            _ => throw Unreadable("a GeoJSON layer is a Feature or a FeatureCollection, whose features carry names"),
        };

        var shapes = new List<LayerShape>();
        foreach (var feature in features)
        {
            var name = feature.TryGetProperty("properties", out var properties)
                       && properties.ValueKind == JsonValueKind.Object
                       && properties.TryGetProperty("name", out var named)
                       && named.ValueKind == JsonValueKind.String
                ? named.GetString()
                : null;
            var parts = new List<Part>();
            if (feature.TryGetProperty("geometry", out var geometry) && geometry.ValueKind == JsonValueKind.Object)
                GeoJsonParts(geometry, parts);
            Add(shapes, name, parts);
        }
        return shapes;
    }

    private static void GeoJsonParts(JsonElement geometry, List<Part> parts)
    {
        switch (geometry.GetProperty("type").GetString())
        {
            case "Polygon":
                parts.Add(GeoJsonArea(geometry.GetProperty("coordinates")));
                break;
            case "MultiPolygon":
                parts.AddRange(geometry.GetProperty("coordinates").EnumerateArray().Select(GeoJsonArea));
                break;
            case "LineString":
                parts.Add(Line.Of(GeoJsonPositions(geometry.GetProperty("coordinates"))));
                break;
            case "MultiLineString":
                parts.AddRange(geometry.GetProperty("coordinates").EnumerateArray().Select(line => Line.Of(GeoJsonPositions(line))));
                break;
            case "GeometryCollection":
                foreach (var member in geometry.GetProperty("geometries").EnumerateArray())
                    GeoJsonParts(member, parts);
                break;
        }
    }

    private static Area GeoJsonArea(JsonElement rings) => Area.Of([.. rings.EnumerateArray().Select(GeoJsonPositions)]);

    private static List<Position> GeoJsonPositions(JsonElement positions) =>
        [.. positions.EnumerateArray().Select(position => Position.At(position[0].GetDouble(), position[1].GetDouble()))];

    private static void Add(List<LayerShape> shapes, string? name, List<Part> parts)
    {
        if (string.IsNullOrWhiteSpace(name) || parts.Count == 0) return;
        shapes.Add(new LayerShape(name.Trim(), Written(parts), parts.Sum(part => part.Corners)));
    }

    private static IEnumerable<XElement> Children(XElement parent, string localName) =>
        parent.Elements().Where(element => element.Name.LocalName == localName);

    private static double Number(string text) => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static SubmissionError Unreadable(string reason) =>
        new(RefusalCode.LayerFileUnreadable, $"The file could not be read as a map layer: {reason}.");

    private static string Written(List<Part> parts)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            if (parts.Count == 1) parts[0].Write(writer);
            else if (parts.All(part => part is Area)) Multi(writer, "MultiPolygon", parts);
            else if (parts.All(part => part is Line)) Multi(writer, "MultiLineString", parts);
            else
            {
                writer.WriteStartObject();
                writer.WriteString("type", "GeometryCollection");
                writer.WriteStartArray("geometries");
                foreach (var part in parts) part.Write(writer);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void Multi(Utf8JsonWriter writer, string type, List<Part> parts)
    {
        writer.WriteStartObject();
        writer.WriteString("type", type);
        writer.WriteStartArray("coordinates");
        foreach (var part in parts) part.WriteCoordinates(writer);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private readonly record struct Position(double Longitude, double Latitude)
    {
        public static Position At(double longitude, double latitude) =>
            double.IsFinite(longitude) && double.IsFinite(latitude)
            && longitude is >= -180 and <= 180 && latitude is >= -90 and <= 90
                ? new Position(longitude, latitude)
                : throw Unreadable("a position lies outside the degrees of longitude and latitude");

        public void Write(Utf8JsonWriter writer)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(Longitude);
            writer.WriteNumberValue(Latitude);
            writer.WriteEndArray();
        }
    }

    private abstract record Part
    {
        public abstract int Corners { get; }

        public abstract void WriteCoordinates(Utf8JsonWriter writer);

        public void Write(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("type", this is Area ? "Polygon" : "LineString");
            writer.WritePropertyName("coordinates");
            WriteCoordinates(writer);
            writer.WriteEndObject();
        }

        protected static void WritePositions(Utf8JsonWriter writer, IReadOnlyList<Position> positions)
        {
            writer.WriteStartArray();
            foreach (var position in positions) position.Write(writer);
            writer.WriteEndArray();
        }
    }

    private sealed record Line(IReadOnlyList<Position> Positions) : Part
    {
        public static Line Of(List<Position> positions) =>
            positions.Count >= 2 ? new Line(positions) : throw Unreadable("a line has fewer than two positions");

        public override int Corners => Positions.Count;

        public override void WriteCoordinates(Utf8JsonWriter writer) => WritePositions(writer, Positions);
    }

    private sealed record Area(IReadOnlyList<IReadOnlyList<Position>> Rings) : Part
    {
        // GeoJSON requires a ring's first position repeated last, which a KML or hand-written file may leave out.
        public static Area Of(List<List<Position>> rings)
        {
            if (rings.Count == 0) throw Unreadable("an outline has no boundary");
            var closed = new List<IReadOnlyList<Position>>();
            foreach (var ring in rings)
            {
                if (ring.Count > 0 && ring[0] != ring[^1]) ring.Add(ring[0]);
                if (ring.Count < 4) throw Unreadable("an outline has fewer than three corners");
                closed.Add(ring);
            }
            return new Area(closed);
        }

        public override int Corners => Rings.Sum(ring => ring.Count - 1);

        public override void WriteCoordinates(Utf8JsonWriter writer)
        {
            writer.WriteStartArray();
            foreach (var ring in Rings) WritePositions(writer, ring);
            writer.WriteEndArray();
        }
    }
}
