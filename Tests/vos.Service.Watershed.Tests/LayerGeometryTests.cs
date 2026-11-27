using System.Text.Json;
using FluentAssertions;
using vos.Service.Watershed.SiteLayers;
using vos.Service.Watershed.Terrain;
using Xunit;

namespace vos.Service.Watershed.Tests;

public class LayerGeometryTests
{
    private static readonly LocalFrame Frame = LocalFrame.Around(MadeUpBorders.Rectangle(100, 100));

    public static double[] At(double eastMetres, double northMetres) =>
    [
        MadeUpBorders.Longitude + eastMetres / MadeUpBorders.MetresPerDegreeOfLongitude,
        MadeUpBorders.Latitude + northMetres / EarthDistances.MetresPerDegreeOfLatitude,
    ];

    public static double[][] Closed(params double[][] corners) => [.. corners, corners[0]];

    public static string GeoJson(string type, object coordinates) => JsonSerializer.Serialize(new { type, coordinates });

    private static LayerGeometry? Read(string stated) => LayerGeometry.Read(JsonDocument.Parse(stated).RootElement, Frame);

    private static readonly double[][] Square = Closed(At(-50, -50), At(50, -50), At(50, 50), At(-50, 50));

    [Fact]
    public void The_frame_puts_a_position_its_metres_east_and_north_of_the_borders_middle()
    {
        var point = Frame.At(new Position(At(30, -20)[1], At(30, -20)[0]));

        point.X.Should().BeApproximately(30, 1e-6);
        point.Y.Should().BeApproximately(-20, 1e-6);
    }

    [Fact]
    public void A_polygon_is_a_shape_in_metres()
    {
        Read(GeoJson("Polygon", new[] { Square })).Should().BeOfType<LayerShape>()
            .Which.Shape.AreaSquareMetres.Should().BeApproximately(10000, 1e-6);
    }

    [Fact]
    public void A_polygon_stated_as_text_is_read_the_same()
    {
        Read(JsonSerializer.Serialize(GeoJson("Polygon", new[] { Square }))).Should().BeOfType<LayerShape>()
            .Which.Shape.AreaSquareMetres.Should().BeApproximately(10000, 1e-6);
    }

    [Fact]
    public void A_polygons_later_rings_are_its_holes()
    {
        var hole = Closed(At(-10, -10), At(10, -10), At(10, 10), At(-10, 10));

        Read(GeoJson("Polygon", new[] { Square, hole })).Should().BeOfType<LayerShape>()
            .Which.Shape.AreaSquareMetres.Should().BeApproximately(9600, 1e-6);
    }

    [Fact]
    public void A_multipolygon_is_one_shape_of_every_part()
    {
        var other = Closed(At(100, 0), At(110, 0), At(110, 10), At(100, 10));

        Read(GeoJson("MultiPolygon", new[] { new[] { Square }, new[] { other } })).Should().BeOfType<LayerShape>()
            .Which.Shape.AreaSquareMetres.Should().BeApproximately(10100, 1e-6);
    }

    [Fact]
    public void A_line_string_is_one_line_in_metres()
    {
        var lines = Read(GeoJson("LineString", new[] { At(0, 0), At(30, 40) })).Should().BeOfType<LayerLines>().Which.Lines;

        lines.Should().ContainSingle().Which.Points.Should().HaveCount(2);
        lines[0].Points[1].X.Should().BeApproximately(30, 1e-6);
        lines[0].Points[1].Y.Should().BeApproximately(40, 1e-6);
    }

    [Fact]
    public void A_multiline_string_is_each_of_its_lines()
    {
        Read(GeoJson("MultiLineString", new[] { new[] { At(0, 0), At(10, 0) }, new[] { At(0, 10), At(10, 10), At(20, 10) } }))
            .Should().BeOfType<LayerLines>().Which.Lines.Select(line => line.Points.Count).Should().Equal(2, 3);
    }

    [Theory]
    [InlineData("""{"type":"Point","coordinates":[-30,40]}""")]
    [InlineData("""{"type":"GeometryCollection","geometries":[]}""")]
    [InlineData("""{"coordinates":[[[-30,40],[-29.9,40],[-29.9,40.1],[-30,40]]]}""")]
    [InlineData("""{"type":"Polygon","coordinates":[[[-30,40],[-29.9,40],[-30,40]]]}""")]
    [InlineData("""{"type":"Polygon","coordinates":[]}""")]
    [InlineData("""{"type":"LineString","coordinates":[[-30,40]]}""")]
    [InlineData("""{"type":"LineString","coordinates":"along the wadi"}""")]
    [InlineData("""{"type":"MultiLineString","coordinates":[[[-30,40],["east","north"]]]}""")]
    [InlineData("\"not a shape\"")]
    [InlineData("42")]
    public void A_geometry_that_is_no_shape_or_line_reads_as_none(string stated)
    {
        Read(stated).Should().BeNull();
    }
}
