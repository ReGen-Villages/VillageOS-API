using FluentAssertions;
using vos.Service.Watershed.Terrain;
using Xunit;
using static vos.Service.Watershed.Tests.GuidelineRain;

namespace vos.Service.Watershed.Tests;

// The expected figures were worked out on the same table by a separate implementation of the same reading, outside this code.
public class RainfallIntensitiesTests
{
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

    [Theory]
    [InlineData(5)]
    [InlineData(60)]
    [InlineData(2000)]
    public void A_table_holding_one_duration_reads_its_intensity_at_any_duration(double minutes) =>
        new RainfallIntensities([new RainfallIntensity(60, 25.35)]).MillimetresPerHourOver(minutes).Should().Be(25.35);
}
