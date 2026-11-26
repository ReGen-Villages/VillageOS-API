using FluentAssertions;
using vos.Service.Watershed.Terrain;
using Xunit;

namespace vos.Service.Watershed.Tests;

public class ProfileLinesTests
{
    [Fact]
    public void Each_line_crosses_the_middle_of_the_box_and_reaches_past_the_site_by_the_grids_margin()
    {
        var (westToEast, southToNorth) = ProfileLines.Through(MadeUpBorders.Rectangle(1700, 700));

        westToEast.SpacingMetres.Should().BeApproximately(2 * 850 * 1.3 / 49, 1e-6);
        westToEast.SiteFromMetres.Should().BeApproximately(850 * 1.3 - 850, 1e-6);
        westToEast.SiteToMetres.Should().BeApproximately(850 * 1.3 + 850, 1e-6);
        southToNorth.SpacingMetres.Should().BeApproximately(2 * 350 * 1.3 / 49, 1e-6);
        southToNorth.SiteFromMetres.Should().BeApproximately(350 * 1.3 - 350, 1e-6);
        southToNorth.SiteToMetres.Should().BeApproximately(350 * 1.3 + 350, 1e-6);
    }

    [Fact]
    public void A_line_across_a_narrow_site_is_held_to_the_shortest_half_length()
    {
        var (westToEast, _) = ProfileLines.Through(MadeUpBorders.Rectangle(100, 100));

        westToEast.SpacingMetres.Should().BeApproximately(2 * 250.0 / 49, 1e-6);
        westToEast.SiteFromMetres.Should().BeApproximately(200, 1e-6);
        westToEast.SiteToMetres.Should().BeApproximately(300, 1e-6);
    }

    [Fact]
    public void The_west_to_east_line_runs_east_along_the_middle_latitude_one_spacing_apart()
    {
        var (westToEast, _) = ProfileLines.Through(MadeUpBorders.Rectangle(1700, 700));
        var points = westToEast.Points;

        points.Should().HaveCount(ProfileLines.PointsAlongALine);
        points.Should().OnlyContain(point => Math.Abs(point.Latitude - MadeUpBorders.Latitude) < 1e-12);
        ((points[1].Longitude - points[0].Longitude) * MadeUpBorders.MetresPerDegreeOfLongitude).Should().BeApproximately(westToEast.SpacingMetres, 1e-6);
        ((points[0].Longitude + points[^1].Longitude) / 2).Should().BeApproximately(MadeUpBorders.Longitude, 1e-12);
        westToEast.LongitudeStepDegrees.Should().BeApproximately(points[1].Longitude - points[0].Longitude, 1e-12);
    }

    [Fact]
    public void The_south_to_north_line_runs_north_along_the_middle_longitude_one_spacing_apart()
    {
        var (_, southToNorth) = ProfileLines.Through(MadeUpBorders.Rectangle(1700, 700));
        var points = southToNorth.Points;

        points.Should().HaveCount(ProfileLines.PointsAlongALine);
        points.Should().OnlyContain(point => Math.Abs(point.Longitude - MadeUpBorders.Longitude) < 1e-12);
        ((points[1].Latitude - points[0].Latitude) * EarthDistances.MetresPerDegreeOfLatitude).Should().BeApproximately(southToNorth.SpacingMetres, 1e-6);
        southToNorth.LatitudeStepDegrees.Should().BeApproximately(points[1].Latitude - points[0].Latitude, 1e-12);
    }
}
