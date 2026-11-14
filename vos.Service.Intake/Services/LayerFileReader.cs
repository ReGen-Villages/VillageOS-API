using System.Buffers;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;
using vos.Service.Intake.Models;

namespace vos.Service.Intake.Services;

public sealed record LayerShape(string Name, string Geometry);

// Only shapes bearing a chosen name are parsed, so a shape a planner did not choose costs no more than reading
// past it. Points and unnamed shapes are left out, since a planner chooses a kind for each shape by its name.
public static class LayerFileReader
{
    private static readonly XmlReaderSettings WithoutExternalReferences = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    public static IReadOnlyList<LayerShape> Read(string fileName, Stream bytes, Func<string, bool> isChosen)
    {
        try
        {
            return Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".kml" => Kml(bytes, isChosen),
                ".kmz" => KmlInside(bytes, isChosen),
                ".geojson" or ".json" => GeoJson(bytes, isChosen),
                ".gpx" => Gpx(bytes, isChosen),
                _ => throw Unreadable("a layer file is KML, KMZ, GeoJSON or GPX"),
            };
        }
        catch (Exception error) when (error is XmlException or JsonException or InvalidDataException or FormatException)
        {
            throw Unreadable(error.Message);
        }
    }

    private static List<LayerShape> Kml(Stream bytes, Func<string, bool> isChosen)
    {
        var shapes = new List<LayerShape>();
        using var reader = XmlReader.Create(bytes, WithoutExternalReferences);
        while (reader.Read())
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Placemark")
                Keep(shapes, NamedShape(reader, isChosen, (child, shape) => KmlGeometry(reader, child, shape)));
        return shapes;
    }

    private static List<LayerShape> KmlInside(Stream bytes, Func<string, bool> isChosen)
    {
        using var archive = new ZipArchive(bytes, ZipArchiveMode.Read, leaveOpen: true);
        var kml = archive.Entries.FirstOrDefault(entry => entry.FullName.EndsWith(".kml", StringComparison.OrdinalIgnoreCase))
                  ?? throw Unreadable("the archive holds no KML file");
        using var unpacking = new UnpackedNoFurtherThanAFile(kml.Open());
        return Kml(unpacking, isChosen);
    }

    private static void KmlGeometry(XmlReader reader, string element, ShapeBeingRead shape)
    {
        switch (element)
        {
            case "Polygon":
                var outer = new List<List<Position>>();
                var inner = new List<List<Position>>();
                EachChild(reader, boundary => EachChild(reader, _ =>
                    (boundary == "outerBoundaryIs" ? outer : inner).Add(shape.Ring(KmlCoordinates(reader, shape)))));
                shape.AddArea([.. outer, .. inner]);
                break;
            case "LineString":
                shape.AddLine(KmlCoordinates(reader, shape));
                break;
            case "LinearRing":
                shape.AddArea([shape.Ring(KmlCoordinates(reader, shape))]);
                break;
            default:
                EachChild(reader, child => KmlGeometry(reader, child, shape));
                break;
        }
    }

    private static List<Position> KmlCoordinates(XmlReader reader, ShapeBeingRead shape)
    {
        var positions = new List<Position>();
        EachChild(reader, child =>
        {
            if (child != "coordinates") return;
            var tuple = new StringBuilder();
            foreach (var piece in TextPieces(reader))
                foreach (var character in piece)
                {
                    if (!char.IsWhiteSpace(character)) tuple.Append(character);
                    else if (tuple.Length > 0) shape.Take(positions, KmlPosition(tuple));
                }
            if (tuple.Length > 0) shape.Take(positions, KmlPosition(tuple));
        });
        return positions;
    }

    private static Position KmlPosition(StringBuilder tuple)
    {
        var values = tuple.ToString().Split(',');
        tuple.Clear();
        return values.Length >= 2
            ? Position.At(Number(values[0]), Number(values[1]))
            : throw Unreadable("a position has no latitude");
    }

    private static List<LayerShape> Gpx(Stream bytes, Func<string, bool> isChosen)
    {
        var shapes = new List<LayerShape>();
        using var reader = XmlReader.Create(bytes, WithoutExternalReferences);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element) continue;
            if (reader.LocalName == "trk")
                Keep(shapes, NamedShape(reader, isChosen, (child, track) =>
                {
                    if (child == "trkseg") track.AddLine(GpxPoints(reader, "trkpt", track));
                }));
            else if (reader.LocalName == "rte")
            {
                var points = new List<Position>();
                var route = NamedShape(reader, isChosen, (child, shape) =>
                {
                    if (child == "rtept") shape.Take(points, GpxPoint(reader, child));
                });
                route?.AddLine(points);
                Keep(shapes, route);
            }
        }
        return shapes;
    }

    private static List<Position> GpxPoints(XmlReader reader, string pointName, ShapeBeingRead shape)
    {
        var positions = new List<Position>();
        EachChild(reader, child =>
        {
            if (child == pointName) shape.Take(positions, GpxPoint(reader, pointName));
        });
        return positions;
    }

    private static Position GpxPoint(XmlReader reader, string pointName) => Position.At(
        Number(reader.GetAttribute("lon") ?? throw Unreadable($"a {pointName} has no longitude")),
        Number(reader.GetAttribute("lat") ?? throw Unreadable($"a {pointName} has no latitude")));

    // KML and GPX give a shape's name before its geometry, so geometry met before any name is read past.
    private static ShapeBeingRead? NamedShape(
        XmlReader reader, Func<string, bool> isChosen, Action<string, ShapeBeingRead> readGeometry)
    {
        ShapeBeingRead? shape = null;
        EachChild(reader, child =>
        {
            if (child == "name")
                shape = NameIfChosen(string.Concat(TextPieces(reader)), isChosen) is { } name ? new ShapeBeingRead(name) : null;
            else if (shape is not null) readGeometry(child, shape);
        });
        return shape;
    }

    // Each child is read to its end before the next, whatever the handler took from it.
    private static void EachChild(XmlReader reader, Action<string> handle)
    {
        if (reader.IsEmptyElement) return;
        var depth = reader.Depth;
        while (reader.Read() && reader.Depth > depth)
        {
            if (reader.NodeType != XmlNodeType.Element) continue;
            var childDepth = reader.Depth;
            handle(reader.LocalName);
            if (reader is { NodeType: XmlNodeType.Element, IsEmptyElement: true }) continue;
            while (!(reader.NodeType == XmlNodeType.EndElement && reader.Depth == childDepth) && reader.Read()) { }
        }
    }

    private static IEnumerable<string> TextPieces(XmlReader reader)
    {
        if (reader.IsEmptyElement) yield break;
        var depth = reader.Depth;
        var buffer = ArrayPool<char>.Shared.Rent(4096);
        try
        {
            while (reader.Read() && reader.Depth > depth)
            {
                if (reader.NodeType is not (XmlNodeType.Text or XmlNodeType.CDATA
                    or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)) continue;
                int read;
                while ((read = reader.ReadValueChunk(buffer, 0, buffer.Length)) > 0) yield return new string(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    // The whole file is held as bytes, because a feature may give its name after its geometry and the reader goes
    // back to the geometry only once the name is chosen.
    private static List<LayerShape> GeoJson(Stream bytes, Func<string, bool> isChosen)
    {
        var file = new byte[bytes.Length];
        bytes.ReadExactly(file);
        var text = (ReadOnlySpan<byte>)file;
        if (text.StartsWith(Encoding.UTF8.Preamble)) text = text[Encoding.UTF8.Preamble.Length..];

        var reader = new Utf8JsonReader(text);
        Next(ref reader);
        var root = reader;
        string? type = null;
        var features = default(Utf8JsonReader);
        while (Next(ref reader) == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("type"u8)) type = StringValue(ref reader);
            else if (reader.ValueTextEquals("features"u8)) features = ValueStart(ref reader);
            else ValueStart(ref reader);
        }

        var shapes = new List<LayerShape>();
        switch (type)
        {
            case "FeatureCollection":
                while (Next(ref features) != JsonTokenType.EndArray)
                {
                    Keep(shapes, GeoJsonFeature(features, isChosen));
                    features.Skip();
                }
                break;
            case "Feature":
                Keep(shapes, GeoJsonFeature(root, isChosen));
                break;
            default:
                throw NotAFeature();
        }
        return shapes;
    }

    private static ShapeBeingRead? GeoJsonFeature(Utf8JsonReader feature, Func<string, bool> isChosen)
    {
        if (feature.TokenType != JsonTokenType.StartObject) throw NotAFeature();
        string? name = null;
        var geometry = default(Utf8JsonReader);
        while (Next(ref feature) == JsonTokenType.PropertyName)
        {
            if (feature.ValueTextEquals("properties"u8)) name = NameAmongProperties(ValueStart(ref feature));
            else if (feature.ValueTextEquals("geometry"u8)) geometry = ValueStart(ref feature);
            else ValueStart(ref feature);
        }
        if (NameIfChosen(name, isChosen) is not { } chosen || geometry.TokenType != JsonTokenType.StartObject) return null;

        var shape = new ShapeBeingRead(chosen);
        GeoJsonParts(geometry, shape);
        return shape;
    }

    private static string? NameAmongProperties(Utf8JsonReader properties)
    {
        if (properties.TokenType != JsonTokenType.StartObject) return null;
        string? name = null;
        while (Next(ref properties) == JsonTokenType.PropertyName)
        {
            if (properties.ValueTextEquals("name"u8)) name = StringValue(ref properties);
            else ValueStart(ref properties);
        }
        return name;
    }

    private static void GeoJsonParts(Utf8JsonReader geometry, ShapeBeingRead shape)
    {
        string? type = null;
        var coordinates = default(Utf8JsonReader);
        var members = default(Utf8JsonReader);
        while (Next(ref geometry) == JsonTokenType.PropertyName)
        {
            if (geometry.ValueTextEquals("type"u8)) type = StringValue(ref geometry);
            else if (geometry.ValueTextEquals("coordinates"u8)) coordinates = ValueStart(ref geometry);
            else if (geometry.ValueTextEquals("geometries"u8)) members = ValueStart(ref geometry);
            else ValueStart(ref geometry);
        }

        switch (type)
        {
            case "Polygon":
                shape.AddArea(GeoJsonRings(ref coordinates, shape));
                break;
            case "MultiPolygon":
                ExpectArray(coordinates);
                while (Next(ref coordinates) != JsonTokenType.EndArray) shape.AddArea(GeoJsonRings(ref coordinates, shape));
                break;
            case "LineString":
                shape.AddLine(GeoJsonPositions(ref coordinates, shape));
                break;
            case "MultiLineString":
                ExpectArray(coordinates);
                while (Next(ref coordinates) != JsonTokenType.EndArray) shape.AddLine(GeoJsonPositions(ref coordinates, shape));
                break;
            case "GeometryCollection":
                ExpectArray(members);
                while (Next(ref members) != JsonTokenType.EndArray)
                {
                    if (members.TokenType != JsonTokenType.StartObject) throw Unreadable("a collection holds a member that is not a geometry");
                    GeoJsonParts(members, shape);
                    members.Skip();
                }
                break;
        }
    }

    private static List<List<Position>> GeoJsonRings(ref Utf8JsonReader rings, ShapeBeingRead shape)
    {
        ExpectArray(rings);
        var read = new List<List<Position>>();
        while (Next(ref rings) != JsonTokenType.EndArray) read.Add(shape.Ring(GeoJsonPositions(ref rings, shape)));
        return read;
    }

    private static List<Position> GeoJsonPositions(ref Utf8JsonReader positions, ShapeBeingRead shape)
    {
        ExpectArray(positions);
        var read = new List<Position>();
        while (Next(ref positions) != JsonTokenType.EndArray)
        {
            ExpectArray(positions);
            var longitude = GeoJsonNumber(ref positions);
            var latitude = GeoJsonNumber(ref positions);
            while (Next(ref positions) != JsonTokenType.EndArray)
                if (positions.TokenType != JsonTokenType.Number) throw NotAPosition();
            shape.Take(read, Position.At(longitude, latitude));
        }
        return read;
    }

    private static double GeoJsonNumber(ref Utf8JsonReader reader) =>
        Next(ref reader) == JsonTokenType.Number ? reader.GetDouble() : throw NotAPosition();

    private static SubmissionError NotAPosition() => Unreadable("a position is not a longitude, a latitude and a height");

    private static void ExpectArray(Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw Unreadable("a geometry's coordinates are not a list");
    }

    private static JsonTokenType Next(ref Utf8JsonReader reader) =>
        reader.Read() ? reader.TokenType : throw Unreadable("the file ends part way through");

    private static Utf8JsonReader ValueStart(scoped ref Utf8JsonReader member)
    {
        Next(ref member);
        var start = member;
        member.Skip();
        return start;
    }

    private static string? StringValue(ref Utf8JsonReader member) =>
        ValueStart(ref member) is { TokenType: JsonTokenType.String } value ? value.GetString() : null;

    private static SubmissionError NotAFeature() =>
        Unreadable("a GeoJSON layer is a Feature or a FeatureCollection, whose features carry names");

    private static string? NameIfChosen(string? name, Func<string, bool> isChosen) =>
        name?.Trim() is { Length: > 0 } trimmed && isChosen(trimmed) ? trimmed : null;

    private static void Keep(List<LayerShape> shapes, ShapeBeingRead? shape)
    {
        if (shape is { Parts.Count: > 0 }) shapes.Add(new LayerShape(shape.Name, Written(shape.Parts)));
    }

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

    private sealed class ShapeBeingRead(string name)
    {
        private int corners;

        public string Name => name;

        public List<Part> Parts { get; } = [];

        // A ring repeats its first position last, so it may hold one position more than the corners allowed.
        public void Take(List<Position> positions, Position position)
        {
            if (positions.Count > SubmissionLimits.MostBoundaryCorners) throw TooManyCorners();
            positions.Add(position);
        }

        // GeoJSON requires a ring's first position repeated last, which a KML or hand-written file may leave out.
        public List<Position> Ring(List<Position> positions)
        {
            if (positions.Count > 0 && positions[0] != positions[^1]) positions.Add(positions[0]);
            if (positions.Count < 4) throw Unreadable("an outline has fewer than three corners");
            Count(positions.Count - 1);
            return positions;
        }

        public void AddArea(List<List<Position>> rings)
        {
            if (rings.Count == 0) throw Unreadable("an outline has no boundary");
            Parts.Add(new Area(rings));
        }

        public void AddLine(List<Position> positions)
        {
            if (positions.Count < 2) throw Unreadable("a line has fewer than two positions");
            Count(positions.Count);
            Parts.Add(new Line(positions));
        }

        private void Count(int more)
        {
            corners += more;
            if (corners > SubmissionLimits.MostBoundaryCorners) throw TooManyCorners();
        }

        private SubmissionError TooManyCorners() =>
            new(RefusalCode.FieldTooMany, $"'{name}' holds at most {SubmissionLimits.MostBoundaryCorners} corners.",
                Refusal.With(("field", name), ("most", SubmissionLimits.MostBoundaryCorners)));
    }

    // The archive is unpacked no further than a file may be, so a small archive cannot make the service read more.
    private sealed class UnpackedNoFurtherThanAFile(Stream unpacking) : Stream
    {
        private long unpacked;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = unpacking.Read(buffer, offset, count);
            unpacked += read;
            if (unpacked > SharedDocumentService.MaximumFileBytes)
                throw new SubmissionError(RefusalCode.FileTooLarge,
                    $"The KML inside the archive is larger than the {SharedDocumentService.MaximumFileBytes / (1024 * 1024)} MB a file may be.",
                    Refusal.With(("megabytes", SharedDocumentService.MaximumFileBytes / (1024 * 1024))));
            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) unpacking.Dispose();
            base.Dispose(disposing);
        }
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
        public override void WriteCoordinates(Utf8JsonWriter writer) => WritePositions(writer, Positions);
    }

    private sealed record Area(IReadOnlyList<IReadOnlyList<Position>> Rings) : Part
    {
        public override void WriteCoordinates(Utf8JsonWriter writer)
        {
            writer.WriteStartArray();
            foreach (var ring in Rings) WritePositions(writer, ring);
            writer.WriteEndArray();
        }
    }
}
