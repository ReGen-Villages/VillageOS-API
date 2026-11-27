using FluentAssertions;
using vos.Service.Watershed.Terrain;
using Xunit;

namespace vos.Service.Watershed.Tests;

// The expected figures are the site-study pages' own Codes.rain, run under Node on the same table.
public class RainfallIntensitiesTests
{
    private static readonly double[] Durations = [1440, 720, 360, 300, 240, 180, 120, 90, 60, 45, 30, 20, 15, 10, 5];
    private static readonly double[] OneInFive = [1.93, 3.68, 6.22, 7.21, 8.62, 10.81, 15.11, 18.66, 25.35, 31.78, 41.68, 53.54, 63.34, 76.98, 97.41];
    private static readonly double[] OneInHundred = [6.1, 11.44, 18.12, 20.66, 24.61, 31.06, 44.32, 53.52, 72.81, 90.83, 111.63, 134.49, 155.29, 188.11, 209.2];

    private static RainfallIntensities Table(double[] millimetresPerHour) =>
        new(Durations.Zip(millimetresPerHour, (minutes, rain) => new RainfallIntensity(minutes, rain)));

    [Theory]
    [InlineData(5, 97.41)]
    [InlineData(10, 76.98)]
    [InlineData(60, 25.35)]
    [InlineData(1440, 1.93)]
    public void A_duration_the_table_holds_is_read_as_held(double minutes, double expected) =>
        Table(OneInFive).MillimetresPerHourOver(minutes).Should().BeApproximately(expected, 1e-9);

    [Theory]
    [InlineData(12.5, 69.1455470383)]
    [InlineData(37, 36.2252200322)]
    public void A_duration_between_two_the_table_holds_is_read_on_a_straight_line_between_their_logarithms(double minutes, double expected) =>
        Table(OneInFive).MillimetresPerHourOver(minutes).Should().BeApproximately(expected, 1e-8);

    [Theory]
    [InlineData(12.5, 169.2723328920)]
    [InlineData(37, 100.3371462349)]
    public void The_one_in_hundred_table_is_read_the_same_way(double minutes, double expected) =>
        Table(OneInHundred).MillimetresPerHourOver(minutes).Should().BeApproximately(expected, 1e-8);

    [Theory]
    [InlineData(3, 97.41)]
    [InlineData(2000, 1.93)]
    public void A_duration_outside_the_table_is_held_to_its_nearest_end(double minutes, double expected) =>
        Table(OneInFive).MillimetresPerHourOver(minutes).Should().BeApproximately(expected, 1e-9);
}
