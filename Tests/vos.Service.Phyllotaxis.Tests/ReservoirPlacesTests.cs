using FluentAssertions;
using vos.Service.Phyllotaxis.Placement;
using Xunit;

namespace vos.Service.Phyllotaxis.Tests;

public class ReservoirPlacesTests
{
    private static readonly ReservoirAssumptions Assumptions = DemoAssumptions.Reservoir;

    // 7 by 5 cells of 30 m rising 0.6 m a cell to the east, so the relief is 3.6 m; column i drains 7 - i cells.
    private static TerrainGrid Plane(double slopePercent = 2, Func<int, int, Ground>? ground = null, double risePerColumn = 0.6) =>
        Grids.Build(7, 5, 30, (column, _) => risePerColumn * column, (_, _) => slopePercent, (column, _) => 7 - column, ground: ground);

    private static double At(TerrainGrid grid, IReadOnlyList<double> scores, int column, int row) => scores[grid.IndexOf(new Cell(column, row))];

    [Theory]
    [InlineData(1, 0.855196)]
    [InlineData(2, 0.706772)]
    [InlineData(3, 0.553104)]
    [InlineData(4, 0.391144)]
    [InlineData(5, 0.214052)]
    [InlineData(6, 0)]
    public void An_inner_cell_scores_a_quarter_of_its_catchment_and_three_quarters_of_its_lowness(int column, double expected)
    {
        var grid = Plane();

        At(grid, ReservoirPlaces.Scores(grid, Assumptions), column, 2).Should().BeApproximately(expected, 1e-6);
    }

    [Fact]
    public void A_cell_on_the_border_scores_four_fifths_so_the_lowest_column_is_not_the_best()
    {
        var grid = Plane();
        var scores = ReservoirPlaces.Scores(grid, Assumptions);

        At(grid, scores, 0, 2).Should().BeApproximately(0.8, 1e-9);
        At(grid, scores, 1, 0).Should().BeApproximately(0.855196 * 0.8, 1e-6);
    }

    [Theory]
    [InlineData(5, 0.855196)]
    [InlineData(7.5, 0.427598)]
    [InlineData(10, 0)]
    public void The_score_falls_from_full_at_five_per_cent_to_nothing_at_ten(double slopePercent, double expected)
    {
        var grid = Plane(slopePercent);

        At(grid, ReservoirPlaces.Scores(grid, Assumptions), 1, 2).Should().BeApproximately(expected, 1e-6);
    }

    [Fact]
    public void Ground_kept_clear_scores_nothing()
    {
        var grid = Plane(ground: (column, row) => column == 1 && row == 2 ? Ground.Floods : Ground.Buildable);

        At(grid, ReservoirPlaces.Scores(grid, Assumptions), 1, 2).Should().Be(0);
    }

    [Fact]
    public void On_a_site_with_less_than_a_metre_of_relief_every_cell_counts_as_lowest()
    {
        var grid = Plane(risePerColumn: 0.1);

        At(grid, ReservoirPlaces.Scores(grid, Assumptions), 1, 2).Should().BeApproximately(0.25 * Math.Log(6) / Math.Log(7) + 0.75, 1e-9);
    }

    [Fact]
    public void The_best_cells_keep_their_spacing_from_every_cell_kept_before_them()
    {
        var grid = Plane();

        var best = ReservoirPlaces.BestCells(grid, ReservoirPlaces.Scores(grid, Assumptions), Assumptions);

        best.Select(scored => scored.Cell).Should().Equal(new Cell(1, 1), new Cell(5, 4));
        best[1].Score.Should().BeApproximately(0.214052 * 0.8, 1e-6);
    }

    [Fact]
    public void The_spacing_between_best_cells_is_at_least_three_cells_on_a_coarse_grid()
    {
        var grid = Grids.Build(7, 5, 60, (column, _) => 0.6 * column, (_, _) => 2, (column, _) => 7 - column);

        var best = ReservoirPlaces.BestCells(grid, ReservoirPlaces.Scores(grid, Assumptions), Assumptions);

        best.Select(scored => scored.Cell).Should().Equal(new Cell(1, 1), new Cell(0, 4), new Cell(3, 4), new Cell(4, 1));
    }

    [Fact]
    public void A_place_whose_circle_crosses_the_border_moves_towards_the_middle_of_the_site_until_it_fits()
    {
        var grid = Grids.Build(11, 11, 30, (_, _) => 0);
        var candidate = new ScoredCell(new Cell(10, 5), 1);

        var places = ReservoirPlaces.Pick(grid, [candidate], Grids.Rectangle(0, 0, 300, 300), new Point(150, 150), null, 0, 25, Assumptions with { PlacesShown = 1 });

        places.Should().ContainSingle();
        places[0].Centre.East.Should().BeApproximately(150 + 0.8 * 150, 1e-9);
        places[0].Centre.North.Should().BeApproximately(150, 1e-9);
        places[0].RadiusMetres.Should().Be(25);
        places[0].Cell.Should().Be(new Cell(9, 5));
    }

    [Fact]
    public void Two_places_keep_their_gap_edge_to_edge()
    {
        var grid = Grids.Build(11, 11, 30, (_, _) => 0);
        var first = new ScoredCell(new Cell(3, 5), 1);
        var tooNear = new ScoredCell(new Cell(6, 5), 0.9);
        var farEnough = new ScoredCell(new Cell(7, 5), 0.8);

        var places = ReservoirPlaces.Pick(grid, [first, tooNear, farEnough], Grids.Rectangle(0, 0, 300, 300), new Point(150, 150), null, 0, 30, Assumptions with { PlacesShown = 2 });

        places.Select(place => place.Cell).Should().Equal(new Cell(3, 5), new Cell(7, 5));
    }

    [Fact]
    public void A_place_too_near_the_village_is_passed_over()
    {
        var grid = Grids.Build(11, 11, 30, (_, _) => 0);
        var nearTheVillage = new ScoredCell(new Cell(5, 7), 1);
        var clearOfIt = new ScoredCell(new Cell(5, 2), 0.5);

        var places = ReservoirPlaces.Pick(grid, [nearTheVillage, clearOfIt], Grids.Rectangle(0, 0, 300, 300), new Point(150, 150), new Point(150, 150), 20, 30, Assumptions with { PlacesShown = 1 });

        places.Select(place => place.Cell).Should().Equal(new Cell(5, 2));
    }

    [Fact]
    public void Short_of_places_the_lowest_buildable_ground_where_a_circle_fits_is_taken()
    {
        var grid = Grids.Build(11, 11, 30, (column, _) => 0.6 * column);

        var places = ReservoirPlaces.Pick(grid, [], Grids.Rectangle(0, 0, 300, 300), new Point(150, 150), null, 0, 30, Assumptions);

        places.Select(place => place.Centre).Should().Equal(new Point(30, 30), new Point(30, 150), new Point(30, 270));
        places.Should().OnlyContain(place => place.HeightMetres == 0.6);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_place_found_by_the_search_is_never_on_ground_kept_clear_or_outside_the_border(bool keptClearRatherThanOutside)
    {
        var grid = Grids.Build(
            11,
            11,
            30,
            (column, _) => 0.6 * column,
            inside: (column, _) => keptClearRatherThanOutside || column != 1,
            ground: (column, _) => keptClearRatherThanOutside && column == 1 ? Ground.Floods : Ground.Buildable);

        var places = ReservoirPlaces.Pick(grid, [], Grids.Rectangle(0, 0, 300, 300), new Point(150, 150), null, 0, 30, Assumptions);

        places.Should().NotBeEmpty();
        places.Should().OnlyContain(place => place.Cell.Column != 1);
    }

    // With a radius of 35 m the lattice's outer points do not fit, and those within 85 m of the village are passed over.
    [Fact]
    public void The_search_keeps_clear_of_the_village_and_inside_the_border()
    {
        var grid = Grids.Build(11, 11, 30, (column, _) => 0.6 * column);

        var places = ReservoirPlaces.Pick(grid, [], Grids.Rectangle(0, 0, 300, 300), new Point(150, 150), new Point(70, 150), 20, 35, Assumptions);

        places.Select(place => place.Centre).Should().Equal(new Point(110, 70), new Point(110, 230), new Point(230, 70));
    }

    [Fact]
    public void A_reservoir_larger_than_the_site_has_no_place()
    {
        var grid = Grids.Build(4, 4, 30, (_, _) => 0);

        ReservoirPlaces.Pick(grid, [new ScoredCell(new Cell(1, 1), 1)], Grids.Rectangle(0, 0, 90, 90), new Point(45, 45), null, 0, 60, Assumptions)
            .Should().BeEmpty();
    }

    [Fact]
    public void A_grid_with_no_cell_inside_the_border_scores_nothing()
    {
        var grid = Grids.Build(3, 3, 30, (_, _) => 0, inside: (_, _) => false);

        ReservoirPlaces.Scores(grid, Assumptions).Should().OnlyContain(score => score == 0);
    }
}
