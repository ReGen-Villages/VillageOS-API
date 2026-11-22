using FluentAssertions;
using vos.Service.Phyllotaxis.Placement;
using Xunit;

namespace vos.Service.Phyllotaxis.Tests;

// 9 by 9 cells of 30 m. The built and core circles are each one cell in radius, so each holds a cell and its four neighbours,
// and the site's own radius is 135 m.
public class CoreSitesTests
{
    private const double BuiltRadius = 30;
    private const double CoreRadius = 30;
    private const double SiteRadius = 135;

    private static TerrainGrid Site(
        Func<int, int, double>? height = null,
        Func<int, int, double>? slope = null,
        Func<int, int, double>? drainage = null,
        Func<int, int, Ground>? ground = null) =>
        Grids.Build(9, 9, 30, height ?? ((_, _) => 10), slope, drainage, ground: ground);

    private static CoreSite Best(TerrainGrid grid, ReservoirCell? reservoir = null) =>
        CoreSites.Best(grid, BuiltRadius, CoreRadius, SiteRadius, reservoir, DemoAssumptions.Core)!;

    [Fact]
    public void On_level_ground_the_middle_of_the_site_wins_with_a_full_score()
    {
        var best = Best(Site());

        best.Cell.Should().Be(new Cell(4, 4));
        best.Score.Should().BeApproximately(1, 1e-12);
        best.Accessible.Should().BeTrue();
        best.HeightMetres.Should().Be(10);
        best.FromMiddleMetres.Should().Be(0);
    }

    [Fact]
    public void Flat_ground_that_fits_the_built_circle_wins_over_steeper_ground_nearer_the_middle()
    {
        var best = Best(Site(slope: (column, _) => column >= 3 ? 3 : 0));

        best.Cell.Should().Be(new Cell(1, 4));
        best.Score.Should().BeApproximately(1 - 0.5 * 90 / 135, 1e-9);
        best.MeanSlopePercent.Should().Be(0);
    }

    [Fact]
    public void A_circle_that_reaches_past_the_grid_counts_the_share_inside_it_squared()
    {
        var grid = Site(slope: (column, _) => column >= 1 ? 30 : 0);
        var scores = CoreSites.Best(grid, BuiltRadius, CoreRadius, SiteRadius, null, DemoAssumptions.Core with { InaccessibleFactor = 1 })!;

        scores.Cell.Should().Be(new Cell(0, 4));
        scores.MeanSlopePercent.Should().Be(7.5);
        scores.Score.Should().BeApproximately(1 / (1 + 7.5 / 3) * (1 - 0.5 * 120 / 135) * 0.8 * 0.8, 1e-9);
    }

    [Theory]
    [InlineData(12, -2, 0.8)]
    [InlineData(17, -7, 0.4)]
    [InlineData(8, 2, 1)]
    public void Ground_below_the_reservoir_scores_less_down_to_two_fifths(double reservoirHeight, double aboveMetres, double factor)
    {
        var reservoir = new Cell(8, 8);
        var grid = Site(height: (column, row) => new Cell(column, row) == reservoir ? reservoirHeight : 10);

        var best = Best(grid, new ReservoirCell(reservoir, 10));

        best.Cell.Should().Be(new Cell(4, 4));
        best.AboveReservoirMetres.Should().Be(aboveMetres);
        best.Score.Should().BeApproximately(factor, 1e-12);
    }

    [Fact]
    public void The_core_is_not_put_on_top_of_the_reservoir()
    {
        var reservoir = new Cell(4, 4);

        var best = Best(Site(), new ReservoirCell(reservoir, 50));

        best.Cell.Should().Be(new Cell(2, 2));
        best.Score.Should().BeApproximately(1 - 0.5 * Math.Sqrt(8) * 30 / 135, 1e-9);
    }

    [Fact]
    public void The_core_keeps_off_a_flow_path_inside_its_own_circle()
    {
        var best = Best(Site(drainage: (column, row) => column == 4 && row == 4 ? 81 : 1));

        best.Cell.Should().Be(new Cell(3, 3));
        best.FlowPath.Should().Be(0);
        best.Score.Should().BeApproximately(1 - 0.5 * Math.Sqrt(2) * 30 / 135, 1e-9);
    }

    [Fact]
    public void A_cell_of_unstable_ground_under_the_built_circle_moves_the_core_off_it()
    {
        var best = Best(Site(ground: (column, row) => column == 4 && row == 5 ? Ground.Unstable : Ground.Buildable));

        best.Cell.Should().Be(new Cell(4, 3));
        best.Score.Should().BeApproximately(1 - 0.5 * 30 / 135, 1e-9);
    }

    [Theory]
    [InlineData(Ground.Unstable, true, false)]
    [InlineData(Ground.Floods, false, true)]
    [InlineData(Ground.ProtectedZone, false, true)]
    public void Where_no_place_avoids_ground_kept_clear_the_core_says_which_ground_it_is_on(Ground kept, bool unstable, bool inKeptClearGround)
    {
        var best = Best(Site(ground: (_, _) => kept));

        best.Accessible.Should().BeFalse();
        best.OnUnstableGround.Should().Be(unstable);
        best.InKeptClearGround.Should().Be(inKeptClearGround);
        best.Score.Should().BeApproximately(0.001 / 6, 1e-12);
    }

    [Fact]
    public void Ground_too_steep_on_average_for_walking_is_not_accessible()
    {
        var best = Best(Site(slope: (_, _) => 5.5));

        best.Accessible.Should().BeFalse();
        best.MeanSlopePercent.Should().Be(5.5);
    }

    [Fact]
    public void More_than_a_tenth_of_the_built_circle_steeper_than_a_ramp_is_not_accessible()
    {
        var best = Best(Site(slope: (column, row) => (column + row) % 2 == 0 ? 0 : 9));

        best.SteepShare.Should().BeGreaterThan(0.1);
        best.Accessible.Should().BeFalse();
    }

    [Fact]
    public void A_site_with_no_cell_inside_its_border_has_no_core()
    {
        var grid = Grids.Build(3, 3, 30, (_, _) => 0, inside: (_, _) => false);

        CoreSites.Best(grid, BuiltRadius, CoreRadius, SiteRadius, null, DemoAssumptions.Core).Should().BeNull();
    }
}
