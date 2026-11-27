using FluentAssertions;
using vos.Service.Stomata.Services;
using Xunit;

namespace vos.Service.Stomata.Tests;

public class HourlyClimateTests
{
    private static readonly DateTime First = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Second = First.AddHours(1);

    private static IReadOnlyList<Reading> Series(params (DateTime At, double Value)[] readings) =>
        [.. readings.Select(reading => new Reading(reading.At, reading.Value))];

    [Fact]
    public void Each_hour_carries_the_six_readings_taken_at_that_instant()
    {
        var hours = HourlyClimate.Join(
            Series((First, 30)), Series((First, 50)), Series((First, 3.4)), Series((First, 100)), Series((First, 20)), Series((First, 8)));

        hours.Should().Equal(new ClimateHour(First, 30, 50, 3.4, 100, 20, 8));
    }

    [Fact]
    public void An_hour_a_series_holds_nothing_for_leaves_that_reading_unknown_and_keeps_the_rest()
    {
        var hours = HourlyClimate.Join(
            Series((First, 30), (Second, 31)), Series((First, 50), (Second, 51)), Series((First, 3.4)),
            Series((First, 100), (Second, 110)), Series((First, 20), (Second, 21)), Series((First, 8), (Second, 9)));

        hours.Should().Equal(new ClimateHour(First, 30, 50, 3.4, 100, 20, 8), new ClimateHour(Second, 31, 51, null, 110, 21, 9));
    }

    [Fact]
    public void Of_two_readings_at_one_instant_the_last_one_received_is_used()
    {
        var hours = HourlyClimate.Join(Series((First, 30), (First, 32)), [], [], [], [], []);

        hours.Should().Equal(new ClimateHour(First, 32, null, null, null, null, null));
    }

    [Fact]
    public void No_history_gives_no_hours()
    {
        HourlyClimate.Join([], [], [], [], [], []).Should().BeEmpty();
    }
}
