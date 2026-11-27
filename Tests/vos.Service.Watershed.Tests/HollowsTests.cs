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

    // Hollow A's first basin: its cell 900 + cell 1's 1,800; its second: 900 + cell 5's 900. Hollow B: 900 + cell 3's 4,500.
    // Cell 2 drains within hollow A, so its area is not counted again.
    [Fact]
    public void Each_basin_takes_its_own_cells_area_and_the_area_gathered_by_each_cell_draining_into_it_from_outside_its_hollow()
    {
        Hollow[] hollows =
        [
            new([2, 3], [0, 1], 2, [new(0, 1, 1)], 4, [3], 1),
            new([4], [0], 1, [], 5, [4], 0),
        ];
        int[] drainsTo = [1, 2, 3, 4, -1, 3];
        double[] gatheredArea = [900, 1800, 2700, 4500, 5400, 900];

        var entering = Hollows.AreaEnteringEachBasin(hollows, drainsTo, gatheredArea, 900);

        entering.Should().HaveCount(2);
        entering[0].Should().Equal(2700, 1800);
        entering[1].Should().Equal(5400);
    }
}
