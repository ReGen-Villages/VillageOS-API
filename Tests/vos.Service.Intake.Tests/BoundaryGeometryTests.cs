using FluentAssertions;
using vos.Service.Intake.Helpers;
using vos.Service.Intake.Models;
using Xunit;

namespace vos.Service.Intake.Tests;

public class BoundaryGeometryTests
{
    private const double DegreeAtEquatorMetres = 111194.9;

    private static BoundaryPoint At(double latitude, double longitude) =>
        new() { Latitude = latitude, Longitude = longitude };

    private static IReadOnlyList<BoundaryPoint> Box(double latitude, double side) =>
    [
        At(latitude, 0), At(latitude, side), At(latitude + side, side), At(latitude + side, 0),
    ];

    [Fact]
    public void A_box_at_the_equator_measures_its_sides_multiplied()
    {
        var sideMetres = 0.01 * DegreeAtEquatorMetres;

        var hectares = BoundaryGeometry.MeasureHectares(Box(latitude: 0, side: 0.01));

        hectares.Should().BeApproximately(sideMetres * sideMetres / 10_000, 0.1);
    }

    [Fact]
    public void The_same_box_encloses_half_as_much_at_sixty_degrees_north()
    {
        var equator = BoundaryGeometry.MeasureHectares(Box(latitude: 0, side: 0.01));

        var high = BoundaryGeometry.MeasureHectares(Box(latitude: 60, side: 0.01));

        high.Should().BeApproximately(equator * 0.5, equator * 0.01,
            "lines of longitude converge, and a measure that treated latitude and longitude as flat "
            + "coordinates would report the same area at both latitudes");
    }

    [Fact]
    public void Which_way_round_the_corners_were_drawn_does_not_change_the_measure()
    {
        var clockwise = Box(latitude: 39.5, side: 0.01);
        var counterClockwise = clockwise.Reverse().ToList();

        BoundaryGeometry.MeasureHectares(counterClockwise)
            .Should().BeApproximately(BoundaryGeometry.MeasureHectares(clockwise), 1e-9);
    }

    [Fact]
    public void A_ring_that_repeats_its_first_corner_measures_the_same()
    {
        var open = Box(latitude: 39.5, side: 0.01);
        var closed = new List<BoundaryPoint>(open) { open[0] };

        BoundaryGeometry.MeasureHectares(closed)
            .Should().BeApproximately(BoundaryGeometry.MeasureHectares(open), 1e-9);
    }

    [Fact]
    public void An_already_closed_ring_is_not_closed_twice()
    {
        var open = Box(latitude: 39.5, side: 0.01);
        var closed = new List<BoundaryPoint>(open) { open[0] };

        BoundaryGeometry.ToGeoJson(closed).Should().Be(BoundaryGeometry.ToGeoJson(open));
    }

    [Fact]
    public void A_coordinate_is_written_with_a_dot_whatever_the_host_reads_numbers_as()
    {
        var culture = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("nl-NL");
        try
        {
            BoundaryGeometry.ToGeoJson([At(39.5, -8.25), At(39.6, -8.25), At(39.6, -8.15)])
                .Should().Contain("-8.25").And.NotContain(",25");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culture;
        }
    }
}
