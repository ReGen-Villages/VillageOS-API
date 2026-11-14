using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using vos.Service.Intake.Models;
using vos.Service.Intake.Services;
using Xunit;

namespace vos.Service.Intake.Tests;

public class LayerFileReaderTests
{
    private const string SpringZoneKml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <kml xmlns="http://www.opengis.net/kml/2.2">
          <Document>
            <Placemark>
              <name> Spring Protection Zone </name>
              <Polygon>
                <outerBoundaryIs><LinearRing><coordinates>
                  8.10,47.20,410 8.12,47.20,412 8.12,47.22,415 8.10,47.20,410
                </coordinates></LinearRing></outerBoundaryIs>
              </Polygon>
            </Placemark>
          </Document>
        </kml>
        """;

    private static IReadOnlyList<LayerShape> Read(string fileName, string text) =>
        LayerFileReader.Read(fileName, new MemoryStream(Encoding.UTF8.GetBytes(text)));

    private static JsonElement Geometry(LayerShape shape) => JsonDocument.Parse(shape.Geometry).RootElement;

    private static string RefusalCodeOf(Action reading) =>
        reading.Should().Throw<SubmissionError>().Which.AsRefusal().Code;

    [Fact]
    public void A_kml_outline_is_read_as_a_polygon_under_its_name_without_its_heights()
    {
        var shape = Read("layers.kml", SpringZoneKml).Should().ContainSingle().Subject;

        shape.Name.Should().Be("Spring Protection Zone");
        shape.Corners.Should().Be(3);
        shape.Geometry.Should().Be("""{"type":"Polygon","coordinates":[[[8.1,47.2],[8.12,47.2],[8.12,47.22],[8.1,47.2]]]}""");
    }

    [Fact]
    public void A_ring_left_open_is_closed()
    {
        const string kml = """
            <kml><Placemark><name>Zone</name><Polygon><outerBoundaryIs><LinearRing>
              <coordinates>8.1,47.2 8.2,47.2 8.2,47.3</coordinates>
            </LinearRing></outerBoundaryIs></Polygon></Placemark></kml>
            """;

        var shape = Read("layers.kml", kml).Single();

        var ring = Geometry(shape).GetProperty("coordinates")[0];
        ring.GetArrayLength().Should().Be(4);
        ring[3].GetRawText().Should().Be(ring[0].GetRawText());
    }

    [Fact]
    public void An_outline_keeps_the_holes_cut_out_of_it()
    {
        var withHole = SpringZoneKml.Replace("</outerBoundaryIs>", """
            </outerBoundaryIs>
            <innerBoundaryIs><LinearRing><coordinates>8.105,47.205 8.11,47.205 8.11,47.21 8.105,47.205</coordinates></LinearRing></innerBoundaryIs>
            """, StringComparison.Ordinal);

        var shape = Read("layers.kml", withHole).Single();

        Geometry(shape).GetProperty("coordinates").GetArrayLength().Should().Be(2);
        shape.Corners.Should().Be(6);
    }

    [Fact]
    public void A_kml_placemark_of_an_outline_and_a_line_is_read_as_a_collection_and_its_point_is_left_out()
    {
        const string kml = """
            <kml xmlns="http://www.opengis.net/kml/2.2"><Placemark><name>Mill Brook</name><MultiGeometry>
              <LineString><coordinates>8.1,47.2 8.2,47.3</coordinates></LineString>
              <Polygon><outerBoundaryIs><LinearRing><coordinates>8.1,47.2 8.2,47.2 8.2,47.3</coordinates></LinearRing></outerBoundaryIs></Polygon>
              <Point><coordinates>8.15,47.25</coordinates></Point>
            </MultiGeometry></Placemark></kml>
            """;

        var geometry = Geometry(Read("brook.kml", kml).Single());

        geometry.GetProperty("type").GetString().Should().Be("GeometryCollection");
        geometry.GetProperty("geometries").EnumerateArray().Select(part => part.GetProperty("type").GetString())
            .Should().Equal("LineString", "Polygon");
    }

    [Fact]
    public void A_kml_ring_given_alone_is_read_as_an_outline()
    {
        const string kml = """
            <kml><Placemark><name>Zone</name><LinearRing><coordinates>8.1,47.2 8.2,47.2 8.2,47.3 8.1,47.2</coordinates></LinearRing></Placemark></kml>
            """;

        var shape = Read("zone.kml", kml).Single();

        Geometry(shape).GetProperty("type").GetString().Should().Be("Polygon");
        shape.Corners.Should().Be(3);
    }

    [Fact]
    public void Two_outlines_in_one_placemark_are_one_multipolygon()
    {
        const string kml = """
            <kml><Placemark><name>Upper Catchment</name><MultiGeometry>
              <Polygon><outerBoundaryIs><LinearRing><coordinates>8.1,47.2 8.2,47.2 8.2,47.3</coordinates></LinearRing></outerBoundaryIs></Polygon>
              <Polygon><outerBoundaryIs><LinearRing><coordinates>9.1,47.2 9.2,47.2 9.2,47.3</coordinates></LinearRing></outerBoundaryIs></Polygon>
            </MultiGeometry></Placemark></kml>
            """;

        var geometry = Geometry(Read("catchment.kml", kml).Single());

        geometry.GetProperty("type").GetString().Should().Be("MultiPolygon");
        geometry.GetProperty("coordinates").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void A_point_and_an_unnamed_shape_are_left_out()
    {
        const string kml = """
            <kml><Document>
              <Placemark><name>Well Head</name><Point><coordinates>8.15,47.25</coordinates></Point></Placemark>
              <Placemark><LineString><coordinates>8.1,47.2 8.2,47.3</coordinates></LineString></Placemark>
              <Placemark><name>  </name><LineString><coordinates>8.1,47.2 8.2,47.3</coordinates></LineString></Placemark>
            </Document></kml>
            """;

        Read("points.kml", kml).Should().BeEmpty();
    }

    [Fact]
    public void A_kmz_is_read_from_the_first_kml_inside_it()
    {
        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            Entry(zip, "images/legend.png", "not a layer");
            Entry(zip, "doc.kml", SpringZoneKml);
            Entry(zip, "other.kml", SpringZoneKml.Replace("Spring Protection Zone", "Second Zone", StringComparison.Ordinal));
        }
        archive.Position = 0;

        LayerFileReader.Read("layers.kmz", archive).Select(shape => shape.Name).Should().Equal("Spring Protection Zone");
    }

    [Fact]
    public void A_kmz_holding_no_kml_is_refused_as_unreadable()
    {
        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
            Entry(zip, "readme.txt", "nothing here");
        archive.Position = 0;

        RefusalCodeOf(() => LayerFileReader.Read("layers.kmz", archive)).Should().Be(RefusalCode.LayerFileUnreadable);
    }

    [Fact]
    public void A_kmz_unpacking_past_the_largest_file_is_refused_for_its_size()
    {
        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = zip.CreateEntry("doc.kml", CompressionLevel.Fastest).Open();
            var spaces = new byte[1024 * 1024];
            Array.Fill(spaces, (byte)' ');
            for (var megabyte = 0; megabyte <= SharedDocumentService.MaximumFileBytes / spaces.Length; megabyte++)
                entry.Write(spaces);
        }
        archive.Position = 0;

        RefusalCodeOf(() => LayerFileReader.Read("layers.kmz", archive)).Should().Be(RefusalCode.FileTooLarge);
    }

    [Fact]
    public void A_file_declaring_its_own_entities_is_refused_without_reading_them()
    {
        const string kml = """
            <?xml version="1.0"?>
            <!DOCTYPE kml [<!ENTITY outside SYSTEM "file:///etc/hosts">]>
            <kml><Placemark><name>&outside;</name><LineString><coordinates>8.1,47.2 8.2,47.3</coordinates></LineString></Placemark></kml>
            """;

        RefusalCodeOf(() => Read("layers.kml", kml)).Should().Be(RefusalCode.LayerFileUnreadable);
    }

    [Fact]
    public void Every_geojson_shape_type_is_read_and_a_point_is_left_out()
    {
        const string geoJson = """
            {"type":"FeatureCollection","features":[
              {"type":"Feature","properties":{"name":"Zone A"},"geometry":{"type":"Polygon","coordinates":[[[8.1,47.2,400],[8.2,47.2],[8.2,47.3],[8.1,47.2]]]}},
              {"type":"Feature","properties":{"name":"Zones"},"geometry":{"type":"MultiPolygon","coordinates":[[[[8.1,47.2],[8.2,47.2],[8.2,47.3],[8.1,47.2]]],[[[9.1,47.2],[9.2,47.2],[9.2,47.3],[9.1,47.2]]]]}},
              {"type":"Feature","properties":{"name":"Mill Brook"},"geometry":{"type":"LineString","coordinates":[[8.1,47.2],[8.2,47.3]]}},
              {"type":"Feature","properties":{"name":"Brooks"},"geometry":{"type":"MultiLineString","coordinates":[[[8.1,47.2],[8.2,47.3]],[[9.1,47.2],[9.2,47.3]]]}},
              {"type":"Feature","properties":{"name":"Mixed"},"geometry":{"type":"GeometryCollection","geometries":[
                {"type":"Point","coordinates":[8.1,47.2]},{"type":"LineString","coordinates":[[8.1,47.2],[8.2,47.3]]}]}},
              {"type":"Feature","properties":{"name":"Well Head"},"geometry":{"type":"Point","coordinates":[8.1,47.2]}},
              {"type":"Feature","properties":{},"geometry":{"type":"LineString","coordinates":[[8.1,47.2],[8.2,47.3]]}}
            ]}
            """;

        var shapes = Read("layers.geojson", geoJson);

        shapes.Select(shape => (shape.Name, Geometry(shape).GetProperty("type").GetString())).Should().Equal(
            ("Zone A", "Polygon"), ("Zones", "MultiPolygon"), ("Mill Brook", "LineString"),
            ("Brooks", "MultiLineString"), ("Mixed", "LineString"));
        shapes[0].Geometry.Should().NotContain("400", "a height is not part of an outline");
    }

    [Fact]
    public void A_geojson_feature_carries_only_its_outline_into_the_model()
    {
        const string geoJson = """
            {"type":"Feature","properties":{"name":"Mill Brook","owner":"someone"},"crs":{"type":"name"},
             "geometry":{"type":"LineString","coordinates":[[8.1,47.2],[8.2,47.3]],"extra":"kept nowhere"}}
            """;

        Read("brook.json", geoJson).Single().Geometry
            .Should().Be("""{"type":"LineString","coordinates":[[8.1,47.2],[8.2,47.3]]}""");
    }

    [Fact]
    public void A_gpx_track_is_a_line_for_each_segment_and_a_route_is_one_line_and_a_waypoint_is_left_out()
    {
        const string gpx = """
            <gpx version="1.1" xmlns="http://www.topografix.com/GPX/1/1">
              <wpt lat="47.2" lon="8.1"><name>Well Head</name></wpt>
              <trk><name>Survey Walk</name>
                <trkseg><trkpt lat="47.2" lon="8.1"/><trkpt lat="47.3" lon="8.2"/></trkseg>
                <trkseg><trkpt lat="47.4" lon="8.3"/><trkpt lat="47.5" lon="8.4"/></trkseg>
              </trk>
              <rte><name>Access Track</name><rtept lat="47.2" lon="8.1"/><rtept lat="47.25" lon="8.15"/></rte>
            </gpx>
            """;

        var shapes = Read("walk.gpx", gpx);

        shapes.Select(shape => (shape.Name, Geometry(shape).GetProperty("type").GetString())).Should().Equal(
            ("Survey Walk", "MultiLineString"), ("Access Track", "LineString"));
        Geometry(shapes[1]).GetProperty("coordinates")[0].GetRawText().Should().Be("[8.1,47.2]", "longitude comes first");
    }

    [Theory]
    [InlineData("layers.shp", "anything")]
    [InlineData("layers.geojson", "{ not json")]
    [InlineData("layers.geojson", """{"type":"Polygon","coordinates":[]}""")]
    [InlineData("layers.kml", "<kml><Placemark><name>A</name>")]
    [InlineData("layers.kml", "<kml><Placemark><name>A</name><LineString><coordinates>8.1,47.2</coordinates></LineString></Placemark></kml>")]
    [InlineData("layers.kml", "<kml><Placemark><name>A</name><LineString><coordinates>8.1,95 8.2,47.3</coordinates></LineString></Placemark></kml>")]
    [InlineData("layers.kml", "<kml><Placemark><name>A</name><LineString><coordinates>east,47.2 8.2,47.3</coordinates></LineString></Placemark></kml>")]
    [InlineData("layers.kml", "<kml><Placemark><name>A</name><LineString><coordinates>8.1 8.2,47.3</coordinates></LineString></Placemark></kml>")]
    [InlineData("layers.kml", "<kml><Placemark><name>A</name><Polygon><outerBoundaryIs><LinearRing><coordinates>8.1,47.2 8.2,47.2</coordinates></LinearRing></outerBoundaryIs></Polygon></Placemark></kml>")]
    [InlineData("walk.gpx", "<gpx><trk><name>A</name><trkseg><trkpt lat=\"47.2\"/><trkpt lat=\"47.3\" lon=\"8.2\"/></trkseg></trk></gpx>")]
    public void A_file_that_is_not_a_layer_this_service_reads_is_refused_as_unreadable(string fileName, string text)
    {
        RefusalCodeOf(() => Read(fileName, text)).Should().Be(RefusalCode.LayerFileUnreadable);
    }

    private static void Entry(ZipArchive zip, string name, string text)
    {
        using var entry = new StreamWriter(zip.CreateEntry(name).Open());
        entry.Write(text);
    }
}
