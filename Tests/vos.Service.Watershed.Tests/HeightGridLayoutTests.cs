using FluentAssertions;
using vos.Service.Watershed.Terrain;
using Xunit;

namespace vos.Service.Watershed.Tests;

public class HeightGridLayoutTests
{
    private static readonly IReadOnlyList<Position> ReferenceBorder =
    [
        new(25.1548913568011, 55.55616063595723), new(25.15129015552567, 55.54932911385871), new(25.14978772652261, 55.5468216600034),
        new(25.14886667062588, 55.54735722283466), new(25.15562986578318, 55.5691377022649), new(25.15707226778431, 55.56848828530388),
        new(25.15754361591318, 55.56296846877041), new(25.15637327045562, 55.56259408448088), new(25.15624859750918, 55.55932941382773),
        new(25.1551934630042, 55.55941873916951), new(25.15471781966161, 55.55817561038458), new(25.15484810477037, 55.55807414688074),
        new(25.15462140660673, 55.55752719634192), new(25.15409802690795, 55.55771982371159), new(25.15378546087098, 55.55684163276631),
    ];

    [Fact]
    public void The_reference_parcel_is_laid_out_as_the_recorded_heights_were_asked_for()
    {
        var layout = HeightGridLayout.Over(ReferenceBorder);
        var recorded = ReferenceParcel.Recorded;

        (layout.Columns, layout.Rows).Should().Be((recorded.Columns, recorded.Rows));
        layout.CellSizeMetres.Should().BeApproximately(recorded.CellSizeMetres, 1e-6);
        layout.Inside.Should().Equal(recorded.Inside);
        layout.Inside.Count(inside => inside).Should().Be(53);
    }

    [Fact]
    public void A_site_takes_its_box_and_a_margin_with_cells_sized_so_the_longer_side_fits_the_most_points()
    {
        var layout = HeightGridLayout.Over(MadeUpBorders.Rectangle(1700, 700));

        layout.CellSizeMetres.Should().BeApproximately(1700 * 1.3 / 27, 1e-6);
        (layout.Columns, layout.Rows).Should().Be((28, 12));
    }

    [Fact]
    public void A_small_site_is_held_to_the_shortest_side_and_the_smallest_cell()
    {
        var layout = HeightGridLayout.Over(MadeUpBorders.Rectangle(100, 100));

        layout.CellSizeMetres.Should().Be(30);
        (layout.Columns, layout.Rows).Should().Be((14, 14));
    }

    [Fact]
    public void A_large_site_is_held_to_the_most_points_along_each_side()
    {
        var layout = HeightGridLayout.Over(MadeUpBorders.Rectangle(10_000, 10_000));

        (layout.Columns, layout.Rows).Should().Be((28, 28));
        layout.CellSizeMetres.Should().BeApproximately(13_000 / 27.0, 1e-6);
    }

    [Fact]
    public void A_narrow_site_keeps_its_shorter_side_at_the_shortest_side()
    {
        var layout = HeightGridLayout.Over(MadeUpBorders.Rectangle(5000, 100));

        (layout.Columns, layout.Rows).Should().Be((28, 3));
    }

    [Fact]
    public void The_points_run_row_by_row_from_the_south_west_one_cell_apart_and_centred_on_the_box()
    {
        var layout = HeightGridLayout.Over(MadeUpBorders.Rectangle(1700, 700));
        var points = layout.Points;

        points.Should().HaveCount(layout.Columns * layout.Rows);
        points[0].Should().Be(new Position(layout.OriginLatitude, layout.OriginLongitude));
        (points[1].Longitude - points[0].Longitude).Should().BeApproximately(layout.LongitudeStepDegrees, 1e-12);
        (points[layout.Columns].Latitude - points[0].Latitude).Should().BeApproximately(layout.LatitudeStepDegrees, 1e-12);
        (layout.LatitudeStepDegrees * EarthDistances.MetresPerDegreeOfLatitude).Should().BeApproximately(layout.CellSizeMetres, 1e-6);
        (layout.LongitudeStepDegrees * MadeUpBorders.MetresPerDegreeOfLongitude).Should().BeApproximately(layout.CellSizeMetres, 1e-6);
        ((points[0].Latitude + points[^1].Latitude) / 2).Should().BeApproximately(MadeUpBorders.Latitude, 1e-12);
        ((points[0].Longitude + points[^1].Longitude) / 2).Should().BeApproximately(MadeUpBorders.Longitude, 1e-12);
    }

    [Fact]
    public void A_point_is_inside_exactly_when_it_lies_within_the_border()
    {
        var border = MadeUpBorders.Rectangle(1700, 700);
        var layout = HeightGridLayout.Over(border);
        var (south, north) = (border.Min(corner => corner.Latitude), border.Max(corner => corner.Latitude));
        var (west, east) = (border.Min(corner => corner.Longitude), border.Max(corner => corner.Longitude));

        var expected = layout.Points.Select(point =>
            point.Latitude > south && point.Latitude < north && point.Longitude > west && point.Longitude < east);

        layout.Inside.Should().Equal(expected);
        layout.Inside.Should().Contain(true).And.Contain(false);
    }
}
