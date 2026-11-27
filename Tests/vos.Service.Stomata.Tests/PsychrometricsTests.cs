using FluentAssertions;
using vos.Service.Stomata.Services;
using Xunit;

namespace vos.Service.Stomata.Tests;

// Checked against ASHRAE Handbook, Fundamentals (2017), chapter 1, Table 3 for saturation pressure, and worked by hand from it.
public class PsychrometricsTests
{
    [Theory]
    [InlineData(30.0, 4.2467)]
    [InlineData(20.0, 2.3392)]
    [InlineData(0.01, 0.6117)]
    [InlineData(-10.0, 0.2600)]
    public void Saturation_pressure_matches_the_handbook_table_over_water_and_over_ice(double celsius, double tableKilopascals)
    {
        Psychrometrics.SaturationPressureKilopascals(celsius).Should().BeApproximately(tableKilopascals, tableKilopascals * 0.001);
    }

    [Theory]
    [InlineData(0.0, 101.325)]
    [InlineData(1500.0, 84.556)]
    public void Pressure_falls_with_elevation_as_the_standard_atmosphere_gives_it(double metres, double kilopascals)
    {
        Psychrometrics.PressureAtElevationKilopascals(metres).Should().BeApproximately(kilopascals, 0.01);
    }

    // 621.945 × 2.12335 / (101.325 − 2.12335), with half the table's 4.2467 kPa at 30 °C.
    [Fact]
    public void Air_at_thirty_degrees_and_half_saturated_at_sea_level_holds_about_thirteen_grams_a_kilogram()
    {
        Psychrometrics.HumidityRatioGramsPerKilogram(30.0, 50.0, 0.0).Should().BeApproximately(13.31, 0.01);
    }

    [Fact]
    public void The_same_air_at_fifteen_hundred_metres_holds_more_water_for_each_kilogram_of_dry_air()
    {
        Psychrometrics.HumidityRatioGramsPerKilogram(30.0, 50.0, 1500.0).Should().BeApproximately(16.02, 0.01);
    }

    [Fact]
    public void Air_with_no_water_vapour_has_a_humidity_ratio_of_nought()
    {
        Psychrometrics.HumidityRatioGramsPerKilogram(25.0, 0.0, 0.0).Should().Be(0.0);
    }

    [Fact]
    public void The_relative_humidity_of_a_humidity_ratio_is_the_one_it_was_worked_out_from()
    {
        var humidityRatio = Psychrometrics.HumidityRatioGramsPerKilogram(30.0, 50.0, 0.0);

        Psychrometrics.RelativeHumidityPercent(30.0, humidityRatio, SeaLevel).Should().BeApproximately(50.0, 1e-9);
    }

    [Fact]
    public void More_water_than_the_air_can_hold_reads_as_saturated()
    {
        Psychrometrics.RelativeHumidityPercent(20.0, 30.0, SeaLevel).Should().Be(100.0);
    }

    // Equation 33 at 22 °C: Ws* = 0.621945 × 2.6448 / (101.325 − 2.6448) = 0.016669, and
    // W = ((2501 − 2.326 × 22) × 0.016669 − 1.006 × 8) / (2501 + 1.86 × 30 − 4.186 × 22) = 13.30 g/kg.
    [Fact]
    public void Air_at_thirty_degrees_holding_thirteen_grams_a_kilogram_has_a_wet_bulb_of_twenty_two_degrees()
    {
        Psychrometrics.WetBulbCelsius(30.0, 13.31, SeaLevel).Should().BeApproximately(22.0, 0.05);
    }

    [Theory]
    [InlineData(25.0)]
    [InlineData(-5.0)]
    public void Saturated_air_has_a_wet_bulb_equal_to_its_air_temperature_over_water_and_over_ice(double celsius)
    {
        var saturated = Psychrometrics.HumidityRatioAtPressureGramsPerKilogram(celsius, 100.0, SeaLevel);

        Psychrometrics.WetBulbCelsius(celsius, saturated, SeaLevel).Should().BeApproximately(celsius, 0.001);
    }

    private const double SeaLevel = 101.325;
}
