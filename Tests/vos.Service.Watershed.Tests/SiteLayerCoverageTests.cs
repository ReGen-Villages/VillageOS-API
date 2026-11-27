using FluentAssertions;
using vos.Service.Watershed.SiteLayers;
using Xunit;
using static vos.Service.Watershed.Tests.PlaneShapeTests;

namespace vos.Service.Watershed.Tests;

public class SiteLayerCoverageTests
{
    private static readonly PlaneShape Site = new(Square);

    private static PlaneLine Line(params (double X, double Y)[] points) => new(Ring(points));

    [Fact]
    public void A_shape_over_a_quarter_of_the_site_holds_its_hectares_and_share_and_is_no_distance_away()
    {
        SiteLayerCoverage.OfShape(Site, new PlaneShape([Polygon(Box(50, 50, 150, 150))]))
            .Should().Be(new ShapeCoverage(0.25, 25, 0));
    }

    [Fact]
    public void A_shape_covering_the_whole_site_holds_all_of_it()
    {
        SiteLayerCoverage.OfShape(Site, new PlaneShape([Polygon(Box(-50, -50, 150, 150))]))
            .Should().Be(new ShapeCoverage(1, 100, 0));
    }

    [Fact]
    public void A_shape_apart_from_the_site_holds_none_of_it_and_is_its_nearest_point_away()
    {
        SiteLayerCoverage.OfShape(Site, new PlaneShape([Polygon(Box(130, 0, 200, 100))]))
            .Should().Be(new ShapeCoverage(0, 0, 30));
    }

    [Fact]
    public void A_shape_touching_the_site_holds_none_of_it_and_is_no_distance_away()
    {
        SiteLayerCoverage.OfShape(Site, new PlaneShape([Polygon(Box(100, 0, 200, 100))]))
            .Should().Be(new ShapeCoverage(0, 0, 0));
    }

    [Fact]
    public void A_share_is_of_the_area_inside_the_border_not_of_its_box()
    {
        var covered = SiteLayerCoverage.OfShape(new PlaneShape(CutAway), new PlaneShape([Polygon(Box(-50, 150, 350, 180))]));

        covered.HectaresInside.Should().BeApproximately(0.6, 1e-12);
        covered.ShareOfSitePercent.Should().BeApproximately(12, 1e-12, "the border holds 300 × 200 less the 100 × 100 cut away");
        covered.DistanceFromSiteMetres.Should().Be(0);
    }

    [Fact]
    public void A_shape_of_two_parts_holds_both_parts_inside()
    {
        SiteLayerCoverage.OfShape(Site, new PlaneShape([Polygon(Box(0, 0, 10, 10)), Polygon(Box(90, 90, 100, 100))]))
            .Should().Be(new ShapeCoverage(0.02, 2, 0));
    }

    [Fact]
    public void A_line_through_the_site_holds_its_metres_inside_and_crosses_the_border()
    {
        SiteLayerCoverage.OfLines(Site, [Line((-50, 50), (150, 50))]).Should().Be(new LineCoverage(100, true, 0));
    }

    [Fact]
    public void A_line_wholly_inside_holds_all_its_length_and_crosses_nothing()
    {
        var covered = SiteLayerCoverage.OfLines(Site, [Line((10, 10), (90, 90))]);

        covered.MetresInside.Should().BeApproximately(80 * Math.Sqrt(2), 1e-9);
        covered.CrossesBorder.Should().BeFalse();
        covered.DistanceFromSiteMetres.Should().Be(0);
    }

    [Fact]
    public void A_line_apart_from_the_site_is_its_nearest_point_away()
    {
        SiteLayerCoverage.OfLines(Site, [Line((140, 0), (140, 100))]).Should().Be(new LineCoverage(0, false, 40));
    }

    // The site-study pages measure only from the line's own points, so they would say about 112 m here.
    [Fact]
    public void A_line_passing_a_corner_of_the_site_is_as_far_as_that_corner()
    {
        SiteLayerCoverage.OfLines(Site, [Line((-100, 150), (200, 150))]).Should().Be(new LineCoverage(0, false, 50));
    }

    [Fact]
    public void A_line_along_the_border_is_not_inside_and_does_not_cross_it()
    {
        SiteLayerCoverage.OfLines(Site, [Line((0, 0), (100, 0))]).Should().Be(new LineCoverage(0, false, 0));
    }

    [Fact]
    public void A_line_ending_on_the_border_is_not_inside_and_does_not_cross_it()
    {
        SiteLayerCoverage.OfLines(Site, [Line((50, -50), (50, 0))]).Should().Be(new LineCoverage(0, false, 0));
    }

    [Fact]
    public void A_line_that_bends_inside_holds_each_leg_inside()
    {
        SiteLayerCoverage.OfLines(Site, [Line((-50, 50), (50, 50), (50, 150))]).Should().Be(new LineCoverage(100, true, 0));
    }

    [Fact]
    public void A_line_across_a_concave_border_holds_only_the_part_inside_it()
    {
        var covered = SiteLayerCoverage.OfLines(new PlaneShape(CutAway), [Line((150, -50), (150, 250))]);

        covered.MetresInside.Should().BeApproximately(100, 1e-9);
        covered.CrossesBorder.Should().BeTrue();
        covered.DistanceFromSiteMetres.Should().Be(0);
    }

    [Fact]
    public void A_feature_of_several_lines_holds_them_all()
    {
        var covered = SiteLayerCoverage.OfLines(Site, [Line((-50, 50), (150, 50)), Line((10, 10), (90, 90))]);

        covered.MetresInside.Should().BeApproximately(100 + 80 * Math.Sqrt(2), 1e-9);
        covered.CrossesBorder.Should().BeTrue();
    }
}
