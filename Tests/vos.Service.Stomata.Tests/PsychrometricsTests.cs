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
}
