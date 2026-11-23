using FluentAssertions;
using vos.Service.Phyllotaxis.Placement;
using Xunit;

namespace vos.Service.Phyllotaxis.Tests;

public class GeometryTests
{
    [Fact]
    public void A_degree_of_latitude_at_the_reference_parcel_is_the_ellipsoids_not_the_equators()
    {
        LocalFrame.MetresPerDegreeOfLatitude(25.13).Should().BeApproximately(110_774.83, 0.01);
        LocalFrame.MetresPerDegreeOfLatitude(0).Should().BeApproximately(110_574.28, 0.01);
    }

    [Fact]
    public void A_degree_of_longitude_shrinks_with_the_parallels_radius()
    {
        LocalFrame.MetresPerDegreeOfLongitude(25.13).Should().BeApproximately(100_843.61, 0.01);
        LocalFrame.MetresPerDegreeOfLongitude(0).Should().BeApproximately(111_319.49, 0.01);
    }

    [Fact]
    public void A_point_is_placed_east_and_north_of_the_origin_in_metres()
    {
        var point = LocalFrame.ToMetres((25.13, 55.5), (25.14, 55.49));

        point.North.Should().BeApproximately(1_107.748, 0.001);
        point.East.Should().BeApproximately(-1_008.436, 0.001);
    }

    [Fact]
    public void A_circles_radius_is_worked_out_from_its_area()
    {
        CircleRadius.OfArea(Math.PI * 100 * 100).Should().BeApproximately(100, 1e-9);
    }

    [Fact]
    public void A_border_holds_the_points_inside_it_and_not_those_in_a_notch()
    {
        var shapeOfAnL = new Border([new Point(0, 0), new Point(100, 0), new Point(100, 40), new Point(40, 40), new Point(40, 100), new Point(0, 100)]);

        shapeOfAnL.Holds(new Point(20, 80)).Should().BeTrue();
        shapeOfAnL.Holds(new Point(80, 20)).Should().BeTrue();
        shapeOfAnL.Holds(new Point(80, 80)).Should().BeFalse();
        shapeOfAnL.Holds(new Point(150, 20)).Should().BeFalse();
    }

    [Fact]
    public void A_border_holds_a_circle_only_when_every_edge_is_at_least_its_radius_away()
    {
        var square = Grids.Rectangle(0, 0, 100, 100);

        square.HoldsCircle(new Point(50, 50), 50).Should().BeTrue();
        square.HoldsCircle(new Point(50, 50), 50.1).Should().BeFalse();
        square.HoldsCircle(new Point(20, 50), 25).Should().BeFalse();
        square.HoldsCircle(new Point(150, 50), 1).Should().BeFalse();
    }

    [Fact]
    public void A_border_whose_ring_repeats_its_first_corner_holds_the_same_circles()
    {
        var closedRing = new Border([new Point(0, 0), new Point(100, 0), new Point(100, 100), new Point(0, 100), new Point(0, 0)]);

        closedRing.HoldsCircle(new Point(50, 50), 50).Should().BeTrue();
        closedRing.HoldsCircle(new Point(20, 50), 25).Should().BeFalse();
    }

    [Fact]
    public void A_grid_whose_lists_do_not_hold_one_value_per_cell_is_refused()
    {
        var building = () => new TerrainGrid(2, 2, 30, [1, 2, 3], [0, 0, 0, 0], [1, 1, 1, 1], [true, true, true, true], [Ground.Buildable, Ground.Buildable, Ground.Buildable, Ground.Buildable]);

        building.Should().Throw<ArgumentException>().WithMessage("*heightMetres*");
    }

    [Fact]
    public void A_point_rounds_to_its_nearest_cell_half_towards_the_east_and_north_and_stays_on_the_grid()
    {
        var grid = Grids.Build(4, 4, 30, (_, _) => 0);

        grid.NearestCell(new Point(45, 15)).Should().Be(new Cell(2, 1));
        grid.NearestCell(new Point(-50, 500)).Should().Be(new Cell(0, 3));
    }
}
