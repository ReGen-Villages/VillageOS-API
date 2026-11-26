using FluentAssertions;
using vos.Service.Watershed.Terrain;
using Xunit;
using static vos.Service.Watershed.Tests.ReferenceParcel;

namespace vos.Service.Watershed.Tests;

public class ReferenceParcelTerrainTests
{
    private static readonly TerrainAnalysisResult Analysed = TerrainAnalysis.Of(Recorded.Surface);

    [Fact]
    public void Every_cells_slope_aspect_drainage_and_hollow_is_what_the_demos_own_code_gives_at_the_true_cell()
    {
        var shown = Recorded.DemoAtTrueCell;

        Analysed.SlopePercent.Should().Equal(shown.SlopePercent, (port, demo) => Math.Abs(port - demo) < 1e-4);
        Analysed.AspectDegrees.Should().Equal(shown.AspectDegrees,
            (port, demo) => demo < 0 ? double.IsNaN(port) : Math.Abs(port - demo) < 1e-3);
        Analysed.DrainageAreaHectares.Should().Equal(
            shown.DrainageCells.Select(cells => cells * Analysed.CellHectares), (port, demo) => Math.Abs(port - demo) < 1e-9);
        Analysed.HollowDepthMetres.Should().Equal(shown.HollowDepthMetres, (port, demo) => Math.Abs(port - demo) < 1e-4);
    }

    [Fact]
    public void Its_figures_are_the_demos_at_the_demos_rounding()
    {
        var figures = Analysed.Figures;
        var shown = Recorded.DemoAtTrueCell.Stats;

        Analysed.CellsCounted.Should().HaveCount(shown.Cells);
        AsTheDemoRounds(figures.AreaHectares).Should().Be(shown.AreaHa);
        (AsTheDemoRounds(figures.LowestMetres), AsTheDemoRounds(figures.HighestMetres), AsTheDemoRounds(figures.MeanMetres))
            .Should().Be((shown.Min, shown.Max, shown.Mean));
        (AsTheDemoRounds(figures.SlopeMeanPercent), AsTheDemoRounds(figures.SlopeMedianPercent),
            AsTheDemoRounds(figures.SlopeNinetiethPercentilePercent)).Should().Be((shown.SlopeMean, shown.SlopeMedian, shown.SlopeP90));
        AsTheDemoRounds(figures.HollowHectares).Should().Be(shown.DepressionHa);
        figures.LowestCell.Should().Be(shown.Lowest.K);
    }

    // The demo works areas out on a cell rounded to whole metres, 108 m against the true 108.27 m, which moves the area alone.
    [Fact]
    public void Of_its_figures_the_demos_whole_metre_cell_moves_only_the_area()
    {
        var figures = Analysed.Figures;
        var shown = Recorded.DemoAtWholeMetreCell.Stats;

        (shown.AreaHa, AsTheDemoRounds(figures.AreaHectares)).Should().Be((61.8, 62.1));
        (shown.Min, shown.Max, shown.Mean, shown.SlopeMean, shown.SlopeMedian, shown.SlopeP90, shown.DepressionHa).Should().Be((
            AsTheDemoRounds(figures.LowestMetres), AsTheDemoRounds(figures.HighestMetres), AsTheDemoRounds(figures.MeanMetres),
            AsTheDemoRounds(figures.SlopeMeanPercent), AsTheDemoRounds(figures.SlopeMedianPercent),
            AsTheDemoRounds(figures.SlopeNinetiethPercentilePercent), AsTheDemoRounds(figures.HollowHectares)));
    }
}
