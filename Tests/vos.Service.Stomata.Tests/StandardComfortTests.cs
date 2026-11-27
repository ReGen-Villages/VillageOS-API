using System.Text.Json;
using FluentAssertions;
using vos.Service.Stomata.Services;
using Xunit;

namespace vos.Service.Stomata.Tests;

// The validation rows are ASHRAE 55-2017's, as pythermalcomfort's validation repository holds them; Recorded/ names the commit.
public class StandardComfortTests
{
    private const double SeaLevelKilopascals = 101.325;
    private const double HighSiteKilopascals = 79.5;

    private static readonly JsonElement Validation = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Recorded", "ashrae-55-validation.json"))).RootElement;

    public static TheoryData<double, double, double, double, double, double, double> PredictedMeanVoteRows() => Rows("pmv", "vr");

    public static TheoryData<double, double, double, double, double, double, double> StandardEffectiveTemperatureRows() => Rows("set", "v");

    [Theory]
    [MemberData(nameof(PredictedMeanVoteRows))]
    public void Fangers_vote_matches_each_ASHRAE_55_validation_row(
        double airCelsius, double meanRadiantCelsius, double relativeHumidityPercent, double airSpeedMetresPerSecond,
        double metabolicRateMet, double clothingClo, double vote)
    {
        StandardComfort.FangerPredictedMeanVote(
                airCelsius, meanRadiantCelsius, relativeHumidityPercent, airSpeedMetresPerSecond, metabolicRateMet, clothingClo)
            .Should().BeApproximately(vote, Tolerance("pmv"));
    }

    [Theory]
    [MemberData(nameof(StandardEffectiveTemperatureRows))]
    public void The_standard_effective_temperature_matches_each_ASHRAE_55_validation_row(
        double airCelsius, double meanRadiantCelsius, double relativeHumidityPercent, double airSpeedMetresPerSecond,
        double metabolicRateMet, double clothingClo, double standardEffectiveCelsius)
    {
        StandardComfort.StandardEffectiveTemperatureCelsius(
                airCelsius, meanRadiantCelsius, relativeHumidityPercent, airSpeedMetresPerSecond, metabolicRateMet, clothingClo, SeaLevelKilopascals)
            .Should().BeApproximately(standardEffectiveCelsius, Tolerance("set"));
    }

    [Fact]
    public void The_standard_effective_temperature_is_worked_at_the_air_pressure_it_is_given()
    {
        StandardComfort.StandardEffectiveTemperatureCelsius(30.0, 30.0, 50.0, 1.0, 1.1, 0.5, HighSiteKilopascals)
            .Should().NotBeApproximately(StandardComfort.StandardEffectiveTemperatureCelsius(30.0, 30.0, 50.0, 1.0, 1.1, 0.5, SeaLevelKilopascals), 0.05);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.2)]
    public void At_or_below_the_elevated_air_speed_an_hours_vote_is_fangers_with_radiant_equal_to_air(double airSpeedMetresPerSecond)
    {
        new StandardComfort().PredictedMeanVote(26.0, 50.0, 0.5, airSpeedMetresPerSecond, 1.1, SeaLevelKilopascals)
            .Should().Be(StandardComfort.FangerPredictedMeanVote(26.0, 26.0, 50.0, airSpeedMetresPerSecond, 1.1, 0.5));
    }

    [Theory]
    [InlineData(SeaLevelKilopascals)]
    [InlineData(HighSiteKilopascals)]
    public void Above_the_elevated_air_speed_an_hours_vote_is_fangers_in_still_air_cooled_by_the_cooling_effect_at_the_sites_pressure(
        double pressureKilopascals)
    {
        var cooling = StandardComfort.CoolingEffectCelsius(28.0, 28.0, 50.0, 1.0, 1.1, 0.5, pressureKilopascals);

        new StandardComfort().PredictedMeanVote(28.0, 50.0, 0.5, 1.0, 1.1, pressureKilopascals).Should().Be(StandardComfort.FangerPredictedMeanVote(
            28.0 - cooling, 28.0 - cooling, 50.0, StandardComfort.StillAirMetresPerSecond, 1.1, 0.5));
    }

    [Fact]
    public void The_cooling_effect_lowers_air_and_radiant_temperature_until_still_air_gives_the_same_standard_effective_temperature()
    {
        var cooling = StandardComfort.CoolingEffectCelsius(28.0, 30.0, 50.0, 1.0, 1.1, 0.5, SeaLevelKilopascals);

        cooling.Should().BePositive();
        StandardComfort.StandardEffectiveTemperatureCelsius(
                28.0 - cooling, 30.0 - cooling, 50.0, StandardComfort.StillAirMetresPerSecond, 1.1, 0.5, SeaLevelKilopascals)
            .Should().BeApproximately(StandardComfort.StandardEffectiveTemperatureCelsius(28.0, 30.0, 50.0, 1.0, 1.1, 0.5, SeaLevelKilopascals), 0.05);
    }

    // Air hotter and more humid than skin gives heat to the body faster as it moves, so moving it cools no one.
    [Fact]
    public void Air_movement_that_warms_the_body_has_no_cooling_effect()
    {
        StandardComfort.CoolingEffectCelsius(44.0, 44.0, 90.0, 1.0, 1.1, 0.5, SeaLevelKilopascals).Should().Be(0.0);
    }

    private static TheoryData<double, double, double, double, double, double, double> Rows(string table, string airSpeed)
    {
        var rows = new TheoryData<double, double, double, double, double, double, double>();
        foreach (var row in Validation.GetProperty(table).EnumerateArray())
        {
            var inputs = row.GetProperty("inputs");
            rows.Add(inputs.GetProperty("tdb").GetDouble(), inputs.GetProperty("tr").GetDouble(), inputs.GetProperty("rh").GetDouble(),
                inputs.GetProperty(airSpeed).GetDouble(), inputs.GetProperty("met").GetDouble(), inputs.GetProperty("clo").GetDouble(),
                row.GetProperty("outputs").GetProperty(table).GetDouble());
        }
        return rows;
    }

    private static double Tolerance(string table) => Validation.GetProperty("tolerance").GetProperty(table).GetDouble();
}
