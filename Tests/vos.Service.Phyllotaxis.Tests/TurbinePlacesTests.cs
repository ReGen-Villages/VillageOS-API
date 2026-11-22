using FluentAssertions;
using vos.Service.Phyllotaxis.Placement;
using Xunit;

namespace vos.Service.Phyllotaxis.Tests;

public class TurbinePlacesTests
{
    private static readonly TurbineAssumptions Assumptions = DemoAssumptions.Turbine;

    // A ridge 41 cells of 30 m long and 3 wide, highest at column 10 of the middle row.
    private static TerrainGrid Ridge(Func<int, int, double>? slope = null, Func<int, int, Ground>? ground = null) =>
        Grids.Build(41, 3, 30, (column, row) => 100 - 0.1 * Math.Abs(column - 10) - 0.01 * Math.Abs(row - 1), slope ?? ((_, _) => 5), ground: ground);

    [Theory]
    [InlineData(116, 464)]
    [InlineData(126, 504)]
    [InlineData(48, 200)]
    public void Turbines_stand_four_rotor_diameters_apart_and_never_nearer_than_two_hundred_metres(double rotorMetres, double spacingMetres)
    {
        TurbinePlaces.SpacingMetres(rotorMetres, Assumptions).Should().Be(spacingMetres);
    }

    [Theory]
    [InlineData(2, 3)]
    [InlineData(5, 5)]
    [InlineData(12, 10)]
    public void At_least_three_places_are_offered_and_at_most_ten(int needed, int offered)
    {
        TurbinePlaces.PlacesWanted(needed, Assumptions).Should().Be(offered);
    }

    [Fact]
    public void The_highest_ground_comes_first_and_the_next_place_is_the_highest_a_full_spacing_away()
    {
        var places = TurbinePlaces.Pick(Ridge(), 3, 464, [], Assumptions);

        places.Select(place => place.Cell).Should().Equal(new Cell(10, 1), new Cell(26, 1));
        places[1].HeightMetres.Should().BeApproximately(98.4, 1e-9);
        places[1].Centre.Should().Be(new Point(780, 30));
    }

    [Fact]
    public void A_summit_as_steep_as_fifteen_per_cent_is_passed_over()
    {
        var places = TurbinePlaces.Pick(Ridge(slope: (column, _) => column == 10 ? 15 : 5), 1, 464, [], Assumptions);

        places.Select(place => place.Cell).Should().Equal(new Cell(9, 1));
    }

    [Fact]
    public void Ground_kept_clear_carries_no_turbine()
    {
        var places = TurbinePlaces.Pick(Ridge(ground: (column, _) => column == 10 ? Ground.Floods : Ground.Buildable), 1, 464, [], Assumptions);

        places.Select(place => place.Cell).Should().Equal(new Cell(9, 1));
    }

    [Fact]
    public void A_place_inside_a_circle_kept_clear_is_passed_over()
    {
        var places = TurbinePlaces.Pick(Ridge(), 1, 464, [new KeepClear(new Point(300, 30), 100)], Assumptions);

        places.Select(place => place.Cell).Should().Equal(new Cell(6, 1));
    }

    [Fact]
    public void The_village_and_each_reservoir_are_kept_clear_by_their_radius_and_the_clearance()
    {
        var grid = Ridge();
        var reservoir = new ReservoirPlace(new Point(605, 35), 20, new Cell(20, 1), 99, 2, 4);

        var keepClear = TurbinePlaces.AroundVillageAndReservoirs(grid, new Cell(10, 1), 144, [reservoir], DemoAssumptions.Clearance);

        keepClear.Should().Equal(new KeepClear(new Point(300, 30), 204), new KeepClear(new Point(600, 30), 80));
    }

    [Fact]
    public void Without_a_village_only_the_reservoirs_are_kept_clear()
    {
        TurbinePlaces.AroundVillageAndReservoirs(Ridge(), null, 144, [], DemoAssumptions.Clearance).Should().BeEmpty();
    }
}
