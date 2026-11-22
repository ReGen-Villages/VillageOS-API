using FluentAssertions;
using vos.Service.Phyllotaxis.Placement;
using Xunit;

namespace vos.Service.Phyllotaxis.Tests;

// The reference parcel's areas: 62.6 ha in all, a built area of 6.5735 ha, and 5.3573 ha of greenhouses beside it.
// Here the site is a square of that area with the village at its middle.
public class WastePlantTests
{
    private static readonly double HalfSide = Math.Sqrt(626_000) / 2;
    private static readonly Border Square = Grids.Rectangle(-HalfSide, -HalfSide, HalfSide, HalfSide);
    private static readonly double BuiltRadius = CircleRadius.OfArea(65_735);
    private static readonly double GreenhouseRadius = CircleRadius.OfArea(65_735 + 53_573);

    private static WastePlace? Place(double windFromDegrees, double builtRadius, double greenhouseRadius) =>
        WastePlant.Place(new Point(0, 0), builtRadius, greenhouseRadius, 300, windFromDegrees, Square, DemoAssumptions.WastePlant);

    [Fact]
    public void The_plant_stands_the_buffer_beyond_the_built_area_twelve_degrees_off_downwind()
    {
        var place = Place(225, BuiltRadius, GreenhouseRadius)!;

        place.BearingDegrees.Should().BeApproximately(33, 1e-9);
        place.FromVillageCentreMetres.Should().BeApproximately(BuiltRadius + 300, 1e-9);
        place.Centre.East.Should().BeApproximately((BuiltRadius + 300) * Math.Sin(33 * Math.PI / 180), 1e-9);
        place.Centre.North.Should().BeApproximately((BuiltRadius + 300) * Math.Cos(33 * Math.PI / 180), 1e-9);
    }

    [Fact]
    public void Where_the_greenhouses_reach_further_than_the_buffer_the_plant_stands_clear_of_them()
    {
        var greenhouses = CircleRadius.OfArea(10_000 + 400_000);

        var place = Place(225, CircleRadius.OfArea(10_000), greenhouses)!;

        place.FromVillageCentreMetres.Should().BeApproximately(greenhouses + 30, 1e-9);
    }

    // The demo pulled the plant back in until it was inside 0.97 of the site's radius, which on the reference parcel left it
    // about 282 m from the homes against a buffer of 300 m.
    [Fact]
    public void When_the_buffer_does_not_fit_inside_the_border_downwind_there_is_no_place()
    {
        Place(271.6164, BuiltRadius, GreenhouseRadius).Should().BeNull();
    }

    [Fact]
    public void The_bearing_wraps_round_north()
    {
        var place = Place(180, 50, GreenhouseRadius)!;

        place.BearingDegrees.Should().BeApproximately(348, 1e-9);
    }
}
