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

    private static IReadOnlyList<LayerShape> Read(string fileName, string text, Func<string, bool>? isChosen = null) =>
        LayerFileReader.Read(fileName, new MemoryStream(Encoding.UTF8.GetBytes(text)), isChosen ?? (_ => true));

    private static JsonElement Geometry(LayerShape shape) => JsonDocument.Parse(shape.Geometry).RootElement;

    private static Refusal RefusalOf(Action reading) => reading.Should().Throw<SubmissionError>().Which.AsRefusal();

    private static string RefusalCodeOf(Action reading) => RefusalOf(reading).Code;

    private static string Positions(int count, double longitude = 8.1) =>
        string.Join(' ', Enumerable.Range(0, count).Select(index => FormattableString.Invariant(
            $"{longitude + 0.01 * Math.Cos(2 * Math.PI * index / count):F6},{47.2 + 0.01 * Math.Sin(2 * Math.PI * index / count):F6}")));

    private static string Outline(int corners, int holeCorners = 0) =>
        $"<Polygon><outerBoundaryIs><LinearRing><coordinates>{Positions(corners)}</coordinates></LinearRing></outerBoundaryIs>"
        + (holeCorners == 0 ? "" : $"<innerBoundaryIs><LinearRing><coordinates>{Positions(holeCorners, 8.1001)}</coordinates></LinearRing></innerBoundaryIs>")
        + "</Polygon>";

    [Fact]
    public void A_kml_outline_is_read_as_a_polygon_under_its_name_without_its_heights()
    {
        var shape = Read("layers.kml", SpringZoneKml).Should().ContainSingle().Subject;

        shape.Name.Should().Be("Spring Protection Zone");
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
    }

    [Fact]
    public void An_outline_s_outer_ring_comes_first_though_the_file_gives_its_hole_first()
    {
        const string kml = """
            <kml><Placemark><name>Zone</name><Polygon>
              <innerBoundaryIs><LinearRing><coordinates>8.105,47.205 8.11,47.205 8.11,47.21</coordinates></LinearRing></innerBoundaryIs>
              <outerBoundaryIs><LinearRing><coordinates>8.1,47.2 8.2,47.2 8.2,47.3</coordinates></LinearRing></outerBoundaryIs>
            </Polygon></Placemark></kml>
            """;

        Geometry(Read("zone.kml", kml).Single()).GetProperty("coordinates")[0][0].GetRawText().Should().Be("[8.1,47.2]");
    }

    [Fact]
    public void A_kml_name_given_as_character_data_is_read()
    {
        const string kml = """
            <kml><Placemark><name><![CDATA[Mill & Brook]]></name><LineString><coordinates>8.1,47.2 8.2,47.3</coordinates></LineString></Placemark></kml>
            """;

        Read("brook.kml", kml).Single().Name.Should().Be("Mill & Brook");
    }

    [Theory]
    [InlineData("<coordinates>8.1,47.2<!--first--> <!--second-->8.2,47.3</coordinates>")]
    [InlineData("<coordinates xml:space=\"preserve\">8.1,47.2<!--first--> <!--second-->8.2,47.3</coordinates>")]
    public void Kml_positions_split_by_comments_and_whitespace_are_two_positions(string coordinates)
    {
        var shape = Read("brook.kml", $"<kml><Placemark><name>Mill Brook</name><LineString>{coordinates}</LineString></Placemark></kml>").Single();

        shape.Geometry.Should().Be("""{"type":"LineString","coordinates":[[8.1,47.2],[8.2,47.3]]}""");
    }

    [Fact]
    public void A_kml_shape_whose_name_follows_its_geometry_is_left_out()
    {
        const string kml = """
            <kml><Placemark><LineString><coordinates>8.1,47.2 8.2,47.3</coordinates></LineString><name>Mill Brook</name></Placemark></kml>
            """;

        Read("brook.kml", kml).Should().BeEmpty();
    }

    [Fact]
    public void An_outline_of_as_many_corners_as_a_border_takes_is_read_since_its_closing_position_is_no_corner()
    {
        var closedRing = $"{Positions(SubmissionLimits.MostBoundaryCorners)} {Positions(1)}";

        Read("zone.kml", $"""
            <kml><Placemark><name>Zone</name><LinearRing><coordinates>{closedRing}</coordinates></LinearRing></Placemark></kml>
            """).Should().ContainSingle();
    }

    [Fact]
    public void A_kml_geometry_s_other_settings_are_not_read_as_positions()
    {
        const string kml = """
            <kml><Placemark><name>Mill Brook</name><LineString><tessellate>1</tessellate><coordinates>8.1,47.2 8.2,47.3</coordinates></LineString></Placemark></kml>
            """;

        Read("brook.kml", kml).Single().Geometry.Should().Be("""{"type":"LineString","coordinates":[[8.1,47.2],[8.2,47.3]]}""");
    }

    [Fact]
    public void An_empty_placemark_or_name_leaves_the_next_shape_to_be_read()
    {
        const string kml = """
            <kml><Document><Placemark><name/></Placemark><Placemark/><Placemark><name>Zone</name><LineString><coordinates>8.1,47.2 8.2,47.3</coordinates></LineString></Placemark>
            </Document></kml>
            """;

        Read("zones.kml", kml).Select(shape => shape.Name).Should().Equal("Zone");
    }

    [Fact]
    public void A_gpx_route_point_s_name_is_not_a_route()
    {
        const string gpx = """
            <gpx><rte><name>Coastline</name>
              <rtept lat="47.2" lon="8.1"><name>Zone</name></rtept><rtept lat="47.3" lon="8.2"/>
            </rte></gpx>
            """;

        Read("walk.gpx", gpx, name => name == "Zone").Should().BeEmpty();
    }

    [Fact]
    public void A_chosen_outline_whose_rings_together_pass_the_corner_limit_is_refused_by_its_name()
    {
        var refusal = RefusalOf(() => Read("zone.kml",
            $"<kml><Placemark><name>Zone</name>{Outline(SubmissionLimits.MostBoundaryCorners - 2, holeCorners: 3)}</Placemark></kml>"));

        refusal.Code.Should().Be(RefusalCode.FieldTooMany);
        refusal.Values!["field"].Should().Be("Zone");
        refusal.Values!["most"].Should().Be(SubmissionLimits.MostBoundaryCorners);
    }

    [Fact]
    public void A_chosen_placemark_whose_lines_together_pass_the_corner_limit_is_refused()
    {
        var half = SubmissionLimits.MostBoundaryCorners / 2;
        var kml = $"""
            <kml><Placemark><name>Brooks</name><MultiGeometry>
              <LineString><coordinates>{Positions(half + 1)}</coordinates></LineString>
              <LineString><coordinates>{Positions(half, 9.1)}</coordinates></LineString>
            </MultiGeometry></Placemark></kml>
            """;

        RefusalCodeOf(() => Read("brooks.kml", kml)).Should().Be(RefusalCode.FieldTooMany);
    }

    [Theory]
    [InlineData("layers.kml", "<kml><Placemark><name>Coastline</name><LineString><coordinates>east,47.2 8.2,47.3</coordinates></LineString></Placemark><Placemark><name>Zone</name><LineString><coordinates>8.1,47.2 8.2,47.3</coordinates></LineString></Placemark></kml>")]
    [InlineData("walk.gpx", "<gpx><trk><name>Coastline</name><trkseg><trkpt lat=\"47.2\"/></trkseg></trk><rte><name>Zone</name><rtept lat=\"47.2\" lon=\"8.1\"/><rtept lat=\"47.3\" lon=\"8.2\"/></rte></gpx>")]
    [InlineData("layers.geojson", """{"type":"FeatureCollection","features":[{"type":"Feature","properties":{"name":"Coastline"},"geometry":{"type":"LineString","coordinates":[["east",47.2]]}},{"type":"Feature","properties":{"name":"Zone"},"geometry":{"type":"LineString","coordinates":[[8.1,47.2],[8.2,47.3]]}}]}""")]
    public void A_shape_not_chosen_is_left_unread(string fileName, string text)
    {
        Read(fileName, text, name => name == "Zone").Select(shape => shape.Name).Should().Equal("Zone");
    }

    [Theory]
    [InlineData("coastline.kml", false)]
    [InlineData("coastline.gpx", false)]
    [InlineData("coastline.geojson", false)]
    [InlineData("coastline.kml", true)]
    [InlineData("coastline.gpx", true)]
    [InlineData("coastline.geojson", true)]
    public void Reading_a_large_shape_costs_less_than_twice_the_file_whether_or_not_it_is_chosen(string fileName, bool chosen)
    {
        const int positions = 200_000;
        var text = new StringBuilder();
        switch (Path.GetExtension(fileName))
        {
            case ".kml":
                text.Append("<kml><Placemark><name>Coastline</name><LineString><coordinates>");
                for (var index = 0; index < positions; index++) text.Append("8.1234567,47.1234567,412 ");
                text.Append("</coordinates></LineString></Placemark></kml>");
                break;
            case ".gpx":
                text.Append("<gpx><trk><name>Coastline</name><trkseg>");
                for (var index = 0; index < positions; index++) text.Append("<trkpt lat=\"47.1234567\" lon=\"8.1234567\"/>");
                text.Append("</trkseg></trk></gpx>");
                break;
            default:
                text.Append("""{"type":"Feature","properties":{"name":"Coastline"},"geometry":{"type":"LineString","coordinates":[""");
                for (var index = 0; index < positions; index++) text.Append(index == 0 ? "" : ",").Append("[8.1234567,47.1234567]");
                text.Append("]}}");
                break;
        }
        var bytes = Encoding.UTF8.GetBytes(text.ToString());

        var before = GC.GetAllocatedBytesForCurrentThread();
        var reading = () => LayerFileReader.Read(fileName, new MemoryStream(bytes), name => chosen && name == "Coastline");
        if (chosen) RefusalCodeOf(() => reading()).Should().Be(RefusalCode.FieldTooMany);
        else reading().Should().BeEmpty();

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(2L * bytes.Length);
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

        LayerFileReader.Read("layers.kmz", archive, _ => true).Select(shape => shape.Name).Should().Equal("Spring Protection Zone");
    }

    [Fact]
    public void A_kmz_holding_no_kml_is_refused_as_unreadable()
    {
        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
            Entry(zip, "readme.txt", "nothing here");
        archive.Position = 0;

        RefusalCodeOf(() => LayerFileReader.Read("layers.kmz", archive, _ => true)).Should().Be(RefusalCode.LayerFileUnreadable);
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

        RefusalCodeOf(() => LayerFileReader.Read("layers.kmz", archive, _ => true)).Should().Be(RefusalCode.FileTooLarge);
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
              {"type":"Feature","properties":{"name":"Nowhere"},"geometry":null},
              {"type":"Feature","properties":{"name":3},"geometry":{"type":"LineString","coordinates":[[8.1,47.2],[8.2,47.3]]}},
              {"type":"Feature","properties":{"name":"Nowhere"}},
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
    public void A_geojson_feature_giving_its_name_after_its_geometry_is_read()
    {
        const string geoJson = """
            {"geometry":{"coordinates":[[8.1,47.2],[8.2,47.3]],"type":"LineString"},"properties":{"name":"Mill Brook"},"type":"Feature"}
            """;

        Read("brook.geojson", geoJson, name => name == "Mill Brook").Single().Geometry
            .Should().Be("""{"type":"LineString","coordinates":[[8.1,47.2],[8.2,47.3]]}""");
    }

    [Fact]
    public void A_geojson_feature_whose_properties_are_null_is_unnamed_whatever_else_it_holds()
    {
        const string geoJson = """
            {"type":"Feature","properties":null,"name":"Mill Brook","geometry":{"type":"LineString","coordinates":[[8.1,47.2],[8.2,47.3]]}}
            """;

        Read("brook.geojson", geoJson).Should().BeEmpty();
    }

    [Fact]
    public void A_geojson_file_beginning_with_a_byte_order_mark_is_read()
    {
        var text = Encoding.UTF8.GetBytes(
            """{"type":"Feature","properties":{"name":"Mill Brook"},"geometry":{"type":"LineString","coordinates":[[8.1,47.2],[8.2,47.3]]}}""");

        LayerFileReader.Read("brook.geojson", new MemoryStream([.. Encoding.UTF8.Preamble, .. text]), _ => true)
            .Should().ContainSingle();
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
    [InlineData("layers.geojson", "")]
    [InlineData("layers.geojson", "[]")]
    [InlineData("layers.geojson", """{"type":"FeatureCollection","features":{}}""")]
    [InlineData("layers.geojson", """{"type":"FeatureCollection","features":[3]}""")]
    [InlineData("layers.geojson", """{"type":"Feature","properties":{"name":"A"},"geometry":{"type":"LineString","coordinates":{}}}""")]
    [InlineData("layers.geojson", """{"type":"Feature","properties":{"name":"A"},"geometry":{"type":"LineString","coordinates":[3,[8.2,47.3]]}}""")]
    [InlineData("layers.geojson", """{"type":"Feature","properties":{"name":"A"},"geometry":{"type":"LineString","coordinates":[[8.1],[8.2,47.3]]}}""")]
    [InlineData("layers.geojson", """{"type":"Feature","properties":{"name":"A"},"geometry":{"type":"LineString","coordinates":[[8.1,"north"],[8.2,47.3]]}}""")]
    [InlineData("layers.geojson", """{"type":"Feature","properties":{"name":"A"},"geometry":{"type":"LineString","coordinates":[[8.1,47.2,"high"],[8.2,47.3]]}}""")]
    [InlineData("layers.geojson", """{"type":"Feature","properties":{"name":"A"},"geometry":{"type":"Polygon","coordinates":3}}""")]
    [InlineData("layers.geojson", """{"type":"Feature","properties":{"name":"A"},"geometry":{"type":"MultiPolygon","coordinates":3}}""")]
    [InlineData("layers.geojson", """{"type":"Feature","properties":{"name":"A"},"geometry":{"type":"MultiLineString","coordinates":3}}""")]
    [InlineData("layers.geojson", """{"type":"Feature","properties":{"name":"A"},"geometry":{"type":"GeometryCollection","geometries":3}}""")]
    [InlineData("layers.geojson", """{"type":"Feature","properties":{"name":"A"},"geometry":{"type":"GeometryCollection","geometries":[3]}}""")]
    [InlineData("layers.geojson", """{"type":"Feature","properties":{"name":"A"},"geometry":{"type":"LineString","coordinates":[[8.1,47.2],[8.2,1e400]]}}""")]
    [InlineData("layers.kmz", "not an archive")]
    [InlineData("walk.gpx", "<gpx><trk><name>A</name><trkseg><trkpt lon=\"8.1\"/><trkpt lat=\"47.3\" lon=\"8.2\"/></trkseg></trk></gpx>")]
    [InlineData("layers.kml", "<kml><Placemark><name>A</name><LinearRing><coordinates></coordinates></LinearRing></Placemark></kml>")]
    [InlineData("layers.geojson", """{"type":"FeatureCollection"}""")]
    [InlineData("layers.kml", "<kml><Placemark><name>A</name><Polygon><extrude>1</extrude></Polygon></Placemark></kml>")]
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
