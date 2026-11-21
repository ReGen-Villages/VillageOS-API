using FluentAssertions;
using vos.Service.Intake.Helpers;
using vos.Service.Intake.Models;
using Xunit;

namespace vos.Service.Intake.Tests;

public class BoundaryGeometryTests
{
    private static BoundaryPoint At(double latitude, double longitude) =>
        new() { Latitude = latitude, Longitude = longitude };

    private static IReadOnlyList<BoundaryPoint> Box(double latitude, double side) =>
    [
        At(latitude, 0), At(latitude, side), At(latitude + side, side), At(latitude + side, 0),
    ];

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
