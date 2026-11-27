using System.Text.Json;
using FluentAssertions;
using vos.Service.Stomata.Services;
using Xunit;

namespace vos.Service.Stomata.Tests;

// Each outline is a GeoJSON Polygon in the psychrometric chart's own units: air temperature across, humidity ratio up.
public class ZoneOutlinesTests
{
    private static readonly (double, double)[] HotAndHumid = [(45.0, 6.0), (30.0, 20.0)];

    private readonly ZoneOutlines _outlines = new(StatedComfort.Strategies(), HotAndHumid);

    [Fact]
    public void The_still_air_outline_runs_round_the_stated_zones_from_their_cool_dry_corner()
    {
        var ring = Ring(_outlines.ComfortInStillAir());

        Corners(ring.Take(3)).Should().Equal((20.0, 0.0), (28.0, 0.0), (28.0, 0.5));
        Corners(ring.TakeLast(2)).Should().Equal((20.0, 0.5), (20.0, 0.0));
        ring.Should().OnlyContain(corner => corner[0] == 20.0 || corner[0] == 28.0);
        ring.Max(corner => corner[1]).Should().Be(12.0);
    }

    [Fact]
    public void The_moving_air_outline_runs_round_the_warmer_stated_zones()
    {
        Corners(Ring(_outlines.ComfortWithAirMovement()).Take(2)).Should().Equal((23.0, 0.0), (31.0, 0.0));
    }

    [Theory]
    [MemberData(nameof(EveryOutline))]
    public void Every_outline_is_one_closed_ring_drawn_counter_clockwise(string outline)
    {
        var ring = Ring(Outline(outline));

        ring[0].Should().Equal(ring[^1]);
        SignedArea(ring).Should().BePositive();
    }

    [Theory]
    [MemberData(nameof(EveryOutline))]
    public void The_same_inputs_give_the_same_text_twice(string outline)
    {
        var again = new ZoneOutlines(StatedComfort.Strategies(), HotAndHumid);

        Outline(outline, again).Should().Be(Outline(outline));
    }

    [Fact]
    public void Every_corner_is_rounded_to_a_hundredth()
    {
        Ring(_outlines.DirectEvaporativeCooling()).Should().OnlyContain(corner =>
            corner.All(value => Math.Round(value, 2) == value));
    }

    // At 8 g/kg the direct zone starts a quarter degree above the summer zone's 28 °C and ends where the wet-bulb reaches
    // that of the zone's hot, humid corner: 1.0209 t + 20.01 kJ/kg meets the corner's 58.6 kJ/kg near 37.8 °C.
    [Fact]
    public void The_direct_evaporative_outline_at_eight_grams_runs_from_above_the_summer_zone_to_the_hottest_comfortable_wet_bulb()
    {
        var row = Ring(_outlines.DirectEvaporativeCooling()).Where(corner => corner[1] == 8.0).Select(corner => corner[0]).ToList();

        row.Min().Should().Be(28.25);
        row.Max().Should().BeInRange(37.5, 38.0);
    }

    // In dry air both reach the site's hottest hour, so the two are compared where direct cooling stops short of it.
    [Fact]
    public void Two_stage_cooling_reaches_hotter_air_than_direct_cooling_does_at_eight_grams()
    {
        Warmest(_outlines.TwoStageEvaporativeCooling(), 8.0).Should().BeGreaterThan(Warmest(_outlines.DirectEvaporativeCooling(), 8.0));
    }

    private static IEnumerable<(double, double)> Corners(IEnumerable<double[]> corners) => corners.Select(corner => (corner[0], corner[1]));

    private static double Warmest(string outline, double humidityRatio) =>
        Ring(outline).Where(corner => corner[1] == humidityRatio).Max(corner => corner[0]);

    [Fact]
    public void A_site_never_warmer_than_the_summer_zone_has_empty_evaporative_outlines()
    {
        var mild = new ZoneOutlines(StatedComfort.Strategies(), [(27.0, 20.0)]);

        mild.DirectEvaporativeCooling().Should().Be(ZoneOutlines.EmptyPolygon);
        mild.TwoStageEvaporativeCooling().Should().Be(ZoneOutlines.EmptyPolygon);
    }

    [Fact]
    public void A_site_with_no_hours_has_empty_evaporative_outlines_and_the_comfort_outlines_its_inputs_give()
    {
        var unrecorded = new ZoneOutlines(StatedComfort.Strategies(), []);

        unrecorded.DirectEvaporativeCooling().Should().Be(ZoneOutlines.EmptyPolygon);
        unrecorded.TwoStageEvaporativeCooling().Should().Be(ZoneOutlines.EmptyPolygon);
        unrecorded.ComfortInStillAir().Should().Be(_outlines.ComfortInStillAir());
        unrecorded.ComfortWithAirMovement().Should().Be(_outlines.ComfortWithAirMovement());
    }

    public static TheoryData<string> EveryOutline() => new("still", "moving", "direct", "two-stage");

    private string Outline(string outline, ZoneOutlines? from = null)
    {
        var outlines = from ?? _outlines;
        return outline switch
        {
            "still" => outlines.ComfortInStillAir(),
            "moving" => outlines.ComfortWithAirMovement(),
            "direct" => outlines.DirectEvaporativeCooling(),
            _ => outlines.TwoStageEvaporativeCooling(),
        };
    }

    private static List<double[]> Ring(string polygon)
    {
        using var document = JsonDocument.Parse(polygon);
        document.RootElement.GetProperty("type").GetString().Should().Be("Polygon");
        var rings = document.RootElement.GetProperty("coordinates");
        rings.GetArrayLength().Should().Be(1);
        return [.. rings[0].EnumerateArray().Select(corner => corner.EnumerateArray().Select(value => value.GetDouble()).ToArray())];
    }

    private static double SignedArea(List<double[]> ring) =>
        ring.Zip(ring.Skip(1), (from, to) => from[0] * to[1] - to[0] * from[1]).Sum() / 2;
}
