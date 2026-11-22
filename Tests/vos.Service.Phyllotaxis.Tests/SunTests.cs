using FluentAssertions;
using vos.Service.Phyllotaxis.Placement;
using Xunit;

namespace vos.Service.Phyllotaxis.Tests;

public class SunTests
{
    private static readonly SunAssumptions Assumptions = DemoAssumptions.Sun;

    private static double Degrees(double radians) => radians * 180 / Math.PI;

    private static double BearingDegrees(double radians) => (Degrees(radians) % 360 + 360) % 360;

    [Theory]
    [InlineData(172, 23.439783)]
    [InlineData(80, -0.403481)]
    [InlineData(355, -23.439783)]
    public void The_declination_follows_the_day_of_the_year(int day, double declination)
    {
        Sun.DeclinationDegrees(day).Should().BeApproximately(declination, 1e-6);
    }

    [Fact]
    public void At_midsummer_noon_the_sun_stands_in_the_south_at_ninety_degrees_less_the_latitude_and_the_declination_apart()
    {
        var noon = Sun.Samples(25, Assumptions).Single(sample => sample.DayOfYear == 172 && sample.Hour == 12);

        Degrees(noon.ElevationRadians).Should().BeApproximately(90 - (25 - 23.439783), 1e-6);
        BearingDegrees(noon.AzimuthRadians).Should().BeApproximately(180, 1e-9);
    }

    [Fact]
    public void At_seven_on_the_spring_equinox_the_sun_is_low_and_just_south_of_east()
    {
        var morning = Sun.Samples(25, Assumptions).Single(sample => sample.DayOfYear == 80 && sample.Hour == 7);

        Degrees(morning.ElevationRadians).Should().BeApproximately(13.391, 1e-3);
        BearingDegrees(morning.AzimuthRadians).Should().BeApproximately(96.834, 1e-3);
    }

    [Fact]
    public void A_sun_lower_than_the_lowest_elevation_is_left_out()
    {
        var samples = Sun.Samples(25, Assumptions);

        samples.Should().NotContain(sample => sample.DayOfYear == 355 && sample.Hour == 7);
        samples.Should().NotContain(sample => sample.DayOfYear == 355 && sample.Hour == 17);
        samples.Should().HaveCount(42);
    }

    [Fact]
    public void Level_ground_with_nothing_round_it_gets_all_the_sun_and_no_shade()
    {
        var grid = Grids.Build(5, 5, 30, (_, _) => 0);

        var sun = Sun.OnTerrain(grid, 25, Assumptions);

        sun.SunShare[grid.IndexOf(new Cell(2, 2))].Should().BeApproximately(1, 1e-12);
        sun.ShadeShare[grid.IndexOf(new Cell(2, 2))].Should().Be(0);
    }

    // A wall 50 m high, 150 m east of the cell, rises 18.43 degrees above it. Of all the samples, only the equinox
    // mornings at seven, at 13.39 and 13.13 degrees, are both lower than that and in the east within the grid.
    [Fact]
    public void A_wall_to_the_east_shades_the_low_morning_suns_it_rises_above()
    {
        var grid = Grids.Build(21, 5, 30, (column, _) => column == 15 ? 50 : 0);
        var samples = Sun.Samples(25, Assumptions);
        var shaded = samples.Where(sample => sample.Hour == 7 && sample.DayOfYear is 80 or 266).Sum(sample => sample.ElevationSine);

        var sun = Sun.OnTerrain(grid, 25, Assumptions);

        var cell = grid.IndexOf(new Cell(10, 2));
        sun.SunShare[cell].Should().BeApproximately(1 - shaded / samples.Sum(sample => sample.ElevationSine), 1e-12);
        sun.SunShare[cell].Should().BeApproximately(0.982486, 1e-6);
        sun.ShadeShare[cell].Should().BeApproximately(0.017514, 1e-6);
    }

    [Fact]
    public void Ground_sloping_away_from_the_sun_gets_less_of_it_than_level_ground()
    {
        var grid = Grids.Build(5, 5, 30, (_, row) => 3.0 * (4 - row));

        var sun = Sun.OnTerrain(grid, 25, Assumptions);

        sun.SunShare[grid.IndexOf(new Cell(2, 2))].Should().BeLessThan(1);
    }

    [Fact]
    public void With_no_sun_above_the_lowest_elevation_no_cell_gets_any_sun_or_shade()
    {
        var grid = Grids.Build(3, 3, 30, (_, _) => 0);

        var sun = Sun.OnTerrain(grid, 25, Assumptions with { LowestElevationSine = 1.1 });

        sun.SunShare.Should().OnlyContain(share => share == 0);
        sun.ShadeShare.Should().OnlyContain(share => share == 0);
    }

    [Fact]
    public void A_cell_outside_the_border_is_not_worked_out()
    {
        var grid = Grids.Build(3, 3, 30, (_, _) => 0, inside: (column, row) => column == 1 && row == 1);

        var sun = Sun.OnTerrain(grid, 25, Assumptions);

        sun.SunShare[grid.IndexOf(new Cell(0, 0))].Should().Be(0);
        sun.SunShare[grid.IndexOf(new Cell(1, 1))].Should().BeApproximately(1, 1e-12);
    }
}
