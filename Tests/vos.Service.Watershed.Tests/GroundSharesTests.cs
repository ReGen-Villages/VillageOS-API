using FluentAssertions;
using vos.Service.Watershed.Terrain;
using Xunit;
using static vos.Service.Watershed.Tests.ReferenceParcel;

namespace vos.Service.Watershed.Tests;

public class GroundSharesTests
{
    private static readonly SlopeClassBand[] TemplateSlopeClasses =
    [
        new(Guid.NewGuid(), 0, 2), new(Guid.NewGuid(), 2, 5), new(Guid.NewGuid(), 5, 10),
        new(Guid.NewGuid(), 10, 20), new(Guid.NewGuid(), 20, null),
    ];

    private static readonly AspectSector[] TemplateAspects =
    [
        .. new double[] { 0, 45, 90, 135, 180, 225, 270, 315 }.Select(centre => new AspectSector(Guid.NewGuid(), centre)),
        new(Guid.NewGuid(), null),
    ];

    private static TerrainAnalysisResult Analysed(params (double Slope, double Aspect)[] cells) => new(
        [.. cells.Select(cell => cell.Slope)], [.. cells.Select(cell => cell.Aspect)], [.. cells.Select(_ => 0.0)],
        [.. cells.Select(_ => 0.0)], [.. Enumerable.Range(0, cells.Length)], 0.5,
        new TerrainFigures(cells.Length * 0.5, 0, 0, 0, 0, 0, 0, 0, 0));

    [Fact]
    public void A_slope_is_in_the_class_whose_lower_bound_holds_it_and_whose_upper_bound_lies_above_it()
    {
        var shares = GroundShares.InSlopeClasses(Analysed((0, 0), (1.99, 0), (2, 0), (19.99, 0), (20, 0), (75, 0)), TemplateSlopeClasses);

        shares.Select(share => share.SharePercent).Should().Equal(100.0 * 2 / 6, 100.0 * 1 / 6, 0, 100.0 * 1 / 6, 100.0 * 2 / 6);
        shares.Select(share => share.AreaHectares).Should().Equal(1, 0.5, 0, 0.5, 1);
        shares.Select(share => share.Term).Should().Equal(TemplateSlopeClasses.Select(band => band.Term));
    }

    [Fact]
    public void An_aspect_is_in_the_sector_around_the_nearest_centre_and_one_on_a_sector_edge_goes_clockwise()
    {
        var shares = GroundShares.FacingAspects(
            Analysed((5, 350), (5, 22.4), (5, 22.5), (5, 337.5), (5, double.NaN), (5, 180)), TemplateAspects);

        shares.Select(share => share.SharePercent).Should().Equal(
            100.0 * 3 / 6, 100.0 * 1 / 6, 0, 0, 100.0 * 1 / 6, 0, 0, 0, 100.0 * 1 / 6);
    }

    [Fact]
    public void The_reference_parcel_is_shared_out_as_the_demo_shares_it_at_the_true_cell()
    {
        var shown = Recorded.DemoAtTrueCell;
        var analysed = TerrainAnalysis.Of(Recorded.Surface);

        var slopes = GroundShares.InSlopeClasses(analysed, TemplateSlopeClasses);
        var aspects = GroundShares.FacingAspects(analysed, TemplateAspects);

        slopes.Select(share => AsTheDemoRounds(share.SharePercent)).Should().Equal(shown.SlopeClasses.Select(each => each.Pct));
        slopes.Select(share => AsTheDemoRounds(share.AreaHectares)).Should().Equal(shown.SlopeClasses.Select(each => each.Ha));
        aspects.Select(share => AsTheDemoRounds(share.SharePercent)).Should().Equal([.. shown.Rose.Pct, shown.Rose.FlatPct]);
    }

    // The demo works areas out on a cell rounded to whole metres, 108 m against the true 108.27 m.
    [Fact]
    public void Of_the_reference_parcels_shares_the_demos_whole_metre_cell_moves_only_the_gentle_class_hectares()
    {
        var slopes = GroundShares.InSlopeClasses(TerrainAnalysis.Of(Recorded.Surface), TemplateSlopeClasses);

        var shown = Recorded.DemoAtWholeMetreCell.SlopeClasses;
        slopes.Select(share => AsTheDemoRounds(share.SharePercent)).Should().Equal(shown.Select(each => each.Pct));
        (shown[1].Ha, AsTheDemoRounds(slopes[1].AreaHectares)).Should().Be((38.5, 38.7));
        slopes.Where((_, index) => index != 1).Select(share => AsTheDemoRounds(share.AreaHectares))
            .Should().Equal(shown.Where((_, index) => index != 1).Select(each => each.Ha));
    }
}
