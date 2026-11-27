using FluentAssertions;
using vos.Service.Watershed.Terrain;
using Xunit;

namespace vos.Service.Watershed.Tests;

public class HollowsTests
{
    private const double Cell = 30;

    private static TerrainSurface Surface(int columns, int rows, params (int Column, int Row)[] pits) => new(
        columns, rows, Cell,
        [.. Enumerable.Range(0, columns * rows).Select(cell => 3 * pits.Min(pit =>
            Math.Sqrt(Math.Pow(cell % columns - pit.Column, 2) + Math.Pow(cell / columns - pit.Row, 2))))],
        [.. Enumerable.Range(0, columns * rows).Select(_ => true)]);

    private static IReadOnlyList<Hollow> HollowsOf(TerrainSurface surface) => Hollows.Of(surface, TerrainAnalysis.Of(surface));

    // Rim cells 4, 36, 44 and 76: 3 × 4 = 12 m, the lowest of the rim.
    [Fact]
    public void A_bowl_whose_rim_has_equally_low_cells_is_one_hollow_of_every_raised_cell()
    {
        var bowl = Surface(9, 9, (4, 4));
        var analysed = TerrainAnalysis.Of(bowl);

        var hollow = Hollows.Of(bowl, analysed).Should().ContainSingle().Subject;

        hollow.Cells.Should().BeEquivalentTo(Enumerable.Range(0, 81).Where(cell => analysed.HollowDepthMetres[cell] > 0));
        hollow.Basins.Should().Be(1);
        hollow.PourPoint.Should().BeOneOf(4, 36, 44, 76);
        hollow.SpillLevel.Should().Be(12);
        hollow.Exits.Should().NotBeEmpty().And.OnlyContain(exit => hollow.Cells.Contains(exit) && !hollow.Cells.Contains(analysed.DrainsTo[exit]));
    }

    // Ridge cell (7, 4): 3 × 3 = 9 m; rim cell (0, 4): 3 × 4 = 12 m.
    [Fact]
    public void Two_pits_split_by_a_ridge_below_the_rim_are_one_hollow_of_two_basins_meeting_at_that_ridge()
    {
        var hollow = HollowsOf(Surface(15, 9, (4, 4), (10, 4))).Should().ContainSingle().Subject;

        hollow.Basins.Should().Be(2);
        var basinOf = hollow.Cells.Zip(hollow.BasinOfCell).ToDictionary(pair => pair.First, pair => pair.Second);
        basinOf[4 * 15 + 4].Should().NotBe(basinOf[4 * 15 + 10]);
        hollow.Ridges.Min(ridge => ridge.Height).Should().BeApproximately(9, 1e-9);
        hollow.SpillLevel.Should().Be(12);
    }

    [Fact]
    public void A_plane_has_no_hollow()
    {
        var plane = new TerrainSurface(9, 9, Cell, [.. Enumerable.Range(0, 81).Select(cell => 1.5 * (cell % 9))], [.. Enumerable.Range(0, 81).Select(_ => true)]);

        HollowsOf(plane).Should().BeEmpty();
    }
}
