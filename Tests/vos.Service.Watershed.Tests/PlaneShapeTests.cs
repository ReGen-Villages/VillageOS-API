using FluentAssertions;
using vos.Service.Watershed.SiteLayers;
using Xunit;

namespace vos.Service.Watershed.Tests;

public class PlaneShapeTests
{
    public static PlanePoint[] Ring(params (double X, double Y)[] corners) => [.. corners.Select(corner => new PlanePoint(corner.X, corner.Y))];

    public static PlanePoint[] Box(double west, double south, double east, double north) =>
        Ring((west, south), (east, south), (east, north), (west, north));

    public static PlanePolygon Polygon(PlanePoint[] outer, params PlanePoint[][] holes) => new(outer, holes);

    public static readonly PlanePolygon[] Square = [Polygon(Box(0, 0, 100, 100))];

    public static readonly PlanePolygon[] CutAway = [Polygon(Ring((0, 0), (300, 0), (300, 200), (200, 200), (200, 100), (100, 100), (100, 200), (0, 200)))];

    private static readonly PlanePoint[] FarAround = Box(-1000, -1000, 1000, 1000);

    public static TheoryData<string, PlanePolygon[], PlanePolygon[], double> Touching => new()
    {
        { "identical outlines", Square, [Polygon(Box(0, 0, 100, 100))], 10000 },
        { "identical outlines, one drawn clockwise from another corner", Square, [Polygon(Ring((100, 100), (100, 0), (0, 0), (0, 100)))], 10000 },
        { "a shared edge, side by side", Square, [Polygon(Box(100, 0, 200, 100))], 0 },
        { "part of a shared edge, side by side", Square, [Polygon(Box(100, 50, 150, 150))], 0 },
        { "three shared edges, one inside the other", Square, [Polygon(Box(0, 0, 50, 100))], 5000 },
        { "a corner on the other's edge, pointing out", Square, [Polygon(Ring((50, 0), (80, -30), (20, -30)))], 0 },
        { "a corner on the other's edge, pointing in", Square, [Polygon(Ring((50, 0), (75, 25), (50, 50), (25, 25)))], 1250 },
        { "corners meeting", Square, [Polygon(Box(100, 100, 200, 200))], 0 },
        { "wholly inside", Square, [Polygon(Box(25, 25, 75, 75))], 2500 },
        { "a hole the other lies across", Square, [Polygon(Box(-50, -50, 150, 150), Box(25, 25, 75, 75))], 7500 },
        { "a hole holding the other", [Polygon(Box(40, 40, 60, 60))], [Polygon(Box(-50, -50, 150, 150), Box(25, 25, 75, 75))], 0 },
        { "a hole sharing an edge with the other", Square, [Polygon(Box(-50, -50, 150, 150), Box(0, 25, 50, 75))], 7500 },
        { "a concave border cut twice by one shape", CutAway, [Polygon(Box(-50, 150, 350, 180))], 6000 },
        { "a concave border cut twice along the floor of its notch", CutAway, [Polygon(Box(-50, 100, 350, 130))], 6000 },
        { "a quarter overlapping", Square, [Polygon(Box(50, 50, 150, 150))], 2500 },
        { "two parts, one inside and one apart", Square, [Polygon(Box(10, 10, 20, 20)), Polygon(Box(110, 10, 120, 20))], 100 },
        { "apart", Square, [Polygon(Box(130, 0, 200, 100))], 0 },
    };

    [Theory]
    [MemberData(nameof(Touching))]
    public void The_area_two_outlines_share_is_the_same_either_way_round(string fixture, PlanePolygon[] one, PlanePolygon[] other, double squareMetres)
    {
        var tolerance = 1e-9 * Math.Max(1, squareMetres);

        new PlaneShape(one).OverlapSquareMetres(new PlaneShape(other)).Should().BeApproximately(squareMetres, tolerance, fixture);
        new PlaneShape(other).OverlapSquareMetres(new PlaneShape(one)).Should().BeApproximately(squareMetres, tolerance, fixture);
    }

    // The outside of an outline is drawn as its own shape, so the two areas are worked out independently.
    [Theory]
    [MemberData(nameof(Touching))]
    public void A_shape_inside_an_outline_and_outside_it_adds_up_to_the_whole_shape(string fixture, PlanePolygon[] one, PlanePolygon[] other, double _)
    {
        foreach (var (shape, outline) in new[] { (one, other), (other, one) })
        {
            var whole = new PlaneShape(shape);
            var inside = whole.OverlapSquareMetres(new PlaneShape(outline));
            var outside = whole.OverlapSquareMetres(new PlaneShape(Outside(outline)));

            (inside + outside).Should().BeApproximately(whole.AreaSquareMetres, 1e-9 * whole.AreaSquareMetres, fixture);
        }
    }

    [Fact]
    public void A_shape_is_its_outer_outline_less_its_holes_however_its_rings_are_drawn()
    {
        new PlaneShape([Polygon(Box(-50, -50, 150, 150), Box(25, 25, 75, 75))]).AreaSquareMetres.Should().Be(37500);
        new PlaneShape([Polygon(Ring((-50, 150), (150, 150), (150, -50), (-50, -50)), Ring((25, 25), (75, 25), (75, 75), (25, 75)))])
            .AreaSquareMetres.Should().Be(37500);
    }

    public static TheoryData<string, PlanePolygon[], double> Apart => new()
    {
        { "30 m east", [Polygon(Box(130, 0, 200, 100))], 30 },
        { "from a corner across a diagonal", [Polygon(Box(130, 140, 200, 200))], 50 },
        { "corners meeting", [Polygon(Box(100, 100, 200, 200))], 0 },
        { "a corner on the edge", [Polygon(Ring((50, 0), (80, -30), (20, -30)))], 0 },
        { "in the shape's hole", [Polygon(Box(-100, -100, 200, 200), Box(-20, -20, 120, 120))], 20 },
    };

    [Theory]
    [MemberData(nameof(Apart))]
    public void Outlines_apart_are_their_nearest_points_apart_either_way_round(string fixture, PlanePolygon[] shape, double metres)
    {
        new PlaneShape(Square).OutlineDistanceMetres(new PlaneShape(shape)).Should().BeApproximately(metres, 1e-9, fixture);
        new PlaneShape(shape).OutlineDistanceMetres(new PlaneShape(Square)).Should().BeApproximately(metres, 1e-9, fixture);
    }

    private static PlanePolygon[] Outside(PlanePolygon[] outline) =>
        [Polygon(FarAround, [.. outline.Select(polygon => polygon.Outer.ToArray())]), .. outline.SelectMany(polygon => polygon.Holes.Select(hole => Polygon([.. hole])))];
}
