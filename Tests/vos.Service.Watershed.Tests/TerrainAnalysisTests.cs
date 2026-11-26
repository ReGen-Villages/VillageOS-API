using FluentAssertions;
using vos.Service.Watershed.Terrain;
using Xunit;

namespace vos.Service.Watershed.Tests;

// Three built shapes whose answers are worked out by hand, so a failure here is in a formula and not in the port.
public class TerrainAnalysisTests
{
    private const int Side = 9;
    private const double Cell = 30;
    private const double CellHectares = Cell * Cell / 10_000;

    private static TerrainSurface Surface(Func<int, int, double> height, Func<int, int, bool>? inside = null) => new(
        Side, Side, Cell,
        [.. Enumerable.Range(0, Side * Side).Select(cell => height(cell % Side, cell / Side))],
        [.. Enumerable.Range(0, Side * Side).Select(cell => inside?.Invoke(cell % Side, cell / Side) ?? true)]);

    private static bool Interior(int column, int row) => column is > 0 and < Side - 1 && row is > 0 and < Side - 1;

    private static double CellsFromTheMiddle(int column, int row) => Math.Sqrt(Math.Pow(column - 4, 2) + Math.Pow(row - 4, 2));

    private static int At(int column, int row) => row * Side + column;

    [Fact]
    public void A_plane_rising_five_percent_to_the_east_has_that_slope_faces_west_and_drains_west()
    {
        var analysed = TerrainAnalysis.Of(Surface((column, _) => 0.05 * Cell * column, Interior));

        foreach (var cell in analysed.CellsCounted)
        {
            analysed.SlopePercent[cell].Should().BeApproximately(5, 1e-9);
            analysed.AspectDegrees[cell].Should().BeApproximately(270, 1e-9);
        }
        analysed.DrainageAreaHectares[At(0, 4)].Should().BeApproximately(Side * CellHectares, 1e-9);
        analysed.DrainageAreaHectares[At(Side - 1, 4)].Should().BeApproximately(CellHectares, 1e-9);
        analysed.HollowDepthMetres.Should().OnlyContain(depth => depth == 0);

        var figures = analysed.Figures;
        figures.AreaHectares.Should().BeApproximately(49 * CellHectares, 1e-9);
        (figures.LowestMetres, figures.HighestMetres).Should().Be((0.05 * Cell * 1, 0.05 * Cell * 7));
        figures.MeanMetres.Should().BeApproximately(0.05 * Cell * 4, 1e-9);
        figures.SlopeMeanPercent.Should().BeApproximately(5, 1e-9);
        figures.SlopeMedianPercent.Should().BeApproximately(5, 1e-9);
        figures.SlopeNinetiethPercentilePercent.Should().BeApproximately(5, 1e-9);
        figures.HollowHectares.Should().Be(0);
        figures.LowestCell.Should().Be(At(1, 1));
    }

    // Two cells east of the peak, the kernel's east column lies √10, 3 and √10 cells from the peak and its west column
    // √2, 1 and √2, each cell falling 3 m, so the slope is 3 × (2√10 + 6 − 2√2 − 2) / (8 × 30), about 9.37 %.
    [Fact]
    public void A_cone_faces_away_from_its_peak_with_the_slope_the_kernel_gives_across_its_curve()
    {
        var analysed = TerrainAnalysis.Of(Surface((column, row) => 100 - 0.1 * Cell * CellsFromTheMiddle(column, row)));

        var expected = 3 * (2 * Math.Sqrt(10) + 6 - 2 * Math.Sqrt(2) - 2) / (8 * Cell) * 100;
        analysed.SlopePercent[At(6, 4)].Should().BeApproximately(expected, 1e-9);
        analysed.AspectDegrees[At(6, 4)].Should().BeApproximately(90, 1e-9);
        analysed.AspectDegrees[At(4, 6)].Should().BeApproximately(0, 1e-9);
        analysed.AspectDegrees[At(2, 4)].Should().BeApproximately(270, 1e-9);
        analysed.AspectDegrees[At(4, 2)].Should().BeApproximately(180, 1e-9);
        double.IsNaN(analysed.AspectDegrees[At(4, 4)]).Should().BeTrue("the peak is flat, so it faces no direction");
        analysed.Figures.HighestMetres.Should().Be(100);
        analysed.Figures.HollowHectares.Should().Be(0);
    }

    // The bowl fills from its lowest rim cell, 12 m in the middle of each side, rising a millimetre a cell inwards, so
    // the middle fills to 12.004 m and holds 12.003 m. Every cell within 115 m of the middle fills by half a metre or
    // more: the 7 × 7 interior less its four corners, 45 cells.
    [Fact]
    public void A_bowl_fills_to_its_lowest_rim_and_holds_a_hollow_of_every_cell_filled_by_half_a_metre_or_more()
    {
        var analysed = TerrainAnalysis.Of(Surface((column, row) => 0.1 * Cell * CellsFromTheMiddle(column, row)));

        analysed.HollowDepthMetres[At(4, 4)].Should().BeApproximately(12.003, 1e-9);
        analysed.HollowDepthMetres.Count(depth => depth >= TerrainAnalysis.HollowAtLeastMetres).Should().Be(45);
        analysed.Figures.HollowHectares.Should().BeApproximately(45 * CellHectares, 1e-9);
        analysed.Figures.LowestCell.Should().Be(At(4, 4));
    }

    [Fact]
    public void A_site_smaller_than_one_cell_is_read_over_the_whole_grid()
    {
        var analysed = TerrainAnalysis.Of(Surface((column, _) => column, (_, _) => false));

        analysed.CellsCounted.Should().HaveCount(Side * Side);
    }
}
