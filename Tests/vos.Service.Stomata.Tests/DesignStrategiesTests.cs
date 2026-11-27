using FluentAssertions;
using vos.Service.Stomata.Services;
using Xunit;

namespace vos.Service.Stomata.Tests;

// Worked by hand against StatedComfort at sea level. The hottest comfortable wet-bulb is that of 28 °C at 12 g/kg, about 20.4 °C.
public class DesignStrategiesTests
{
    private readonly DesignStrategies _strategies = StatedComfort.Strategies();

    [Theory]
    [InlineData(25.0, 40.0, DesignStrategy.ComfortableAsItIs)]           // in the summer still-air zone and a moving-air one; 7.9 g/kg
    [InlineData(29.5, 30.0, DesignStrategy.WithAirMovement)]             // above the still-air zones, inside 27–31 °C; 7.7 g/kg
    [InlineData(25.0, 90.0, DesignStrategy.DehumidificationNeeded)]      // the right temperature at 17.9 g/kg
    [InlineData(29.5, 80.0, DesignStrategy.DehumidificationNeeded)]      // comfortable with air movement but at 21.0 g/kg
    [InlineData(15.0, 50.0, DesignStrategy.HeatingNeeded)]               // below the winter zone's 20 °C
    [InlineData(35.0, 20.0, DesignStrategy.DirectEvaporativeCooling)]    // 7.0 g/kg, wet-bulb about 18.8 °C
    [InlineData(42.0, 15.7, DesignStrategy.TwoStageEvaporativeCooling)]  // 8.0 g/kg, wet-bulb 21.5 °C; after the indirect stage 29.7 °C and 17.9 °C
    [InlineData(35.0, 60.0, DesignStrategy.CoolingSystemNeeded)]         // 21.4 g/kg, wet-bulb about 28 °C
    public void An_hour_goes_into_the_first_group_it_fits(double airCelsius, double relativeHumidityPercent, DesignStrategy group)
    {
        _strategies.Of(airCelsius, relativeHumidityPercent).Should().Be(group);
    }

    [Fact]
    public void The_hottest_comfortable_wet_bulb_is_that_of_the_summer_zones_hot_humid_corner()
    {
        _strategies.HottestComfortableWetBulbCelsius.Should().BeApproximately(
            Psychrometrics.WetBulbCelsius(28.0, 12.0, StatedComfort.Assumptions.PressureKilopascals), 0.01);
    }

    [Theory]
    [InlineData(0.0, 20.0, 28.0)]
    [InlineData(12.0, 20.0, 28.0)]
    public void The_still_air_zone_at_a_humidity_ratio_runs_from_the_winter_zones_cool_edge_to_the_summer_zones_warm_edge(
        double humidityRatio, double coolEdge, double warmEdge)
    {
        var (cool, warm) = _strategies.ZoneAt(humidityRatio, StatedComfort.Assumptions.StillAirMetresPerSecond);

        cool.Should().BeApproximately(coolEdge, 0.001);
        warm.Should().BeApproximately(warmEdge, 0.001);
    }

    [Fact]
    public void The_moving_air_zone_is_three_degrees_warmer_in_the_stated_model()
    {
        var (cool, warm) = _strategies.ZoneAt(6.0, StatedComfort.Assumptions.AirMovementMetresPerSecond);

        cool.Should().BeApproximately(23.0, 0.001);
        warm.Should().BeApproximately(31.0, 0.001);
    }
}
