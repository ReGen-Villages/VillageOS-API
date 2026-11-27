using System.Text.Json;
using FluentAssertions;
using vos.Service.Watershed.Terrain;
using Xunit;
using static vos.Service.Watershed.Tests.ReferenceParcel;

namespace vos.Service.Watershed.Tests;

// The recorded classes are the site-study pages' own storm-check.js, run under Node on the recorded heights at the true cell.
public class ReferenceParcelRunoffTests
{
    private sealed record DemoRunoff(double?[] OneInFive, double?[] OneInHundred, double?[] OneInHundredBuiltOver);

    private static readonly DemoRunoff Demo = JsonSerializer.Deserialize<DemoRunoff>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Recorded", "reference-parcel-runoff.json")),
        new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static readonly RunoffInputs Inputs = new(0.2, 0.85, 0.15, 0.6, 0.05, 0.05, 10, 130, 0.002, 0.003);

    private static readonly IReadOnlyList<RunoffClassRule> Classes =
    [
        new(1, 0, new Dictionary<string, double>(), []),
        new(2, 1, new Dictionary<string, double> { ["shareOfLargestFlowAtLeast"] = 0.05, ["catchmentAboveCells"] = 1.5 }, []),
        new(3, 2, new Dictionary<string, double> { ["shareOfLargestFlowAtLeast"] = 0.05, ["catchmentAboveCells"] = 1.5 },
            [new Dictionary<string, double> { ["flowDepthAboveMetres"] = 0.3 }, new Dictionary<string, double> { ["depthTimesVelocityAboveSquareMetresPerSecond"] = 0.3 }]),
        new(4, 3, new Dictionary<string, double> { ["hollowAtLeastMetres"] = 0.5, ["standingWaterAtLeastMetres"] = 0.05 }, []),
    ];

    private static DesignStormRain Storm(double multiplier, double[] rain) => new(multiplier, GuidelineRain.Table(rain));

    public static TheoryData<string> Grids() => new("one in five", "one in a hundred", "one in a hundred built over");

    [Theory]
    [MemberData(nameof(Grids))]
    public void Every_cell_inside_the_border_takes_the_class_the_demos_own_code_gives(string grid)
    {
        var (storm, builtOver, expected) = grid switch
        {
            "one in five" => (Storm(1, GuidelineRain.OneInFive), false, Demo.OneInFive),
            "one in a hundred" => (Storm(1.25, GuidelineRain.OneInHundred), false, Demo.OneInHundred),
            _ => (Storm(1.25, GuidelineRain.OneInHundred), true, Demo.OneInHundredBuiltOver),
        };

        var codes = StormRunoff.GridCodesOf(Recorded.Surface, TerrainAnalysis.Of(Recorded.Surface), Inputs, storm, builtOver, Classes);

        codes.Select(code => double.IsNaN(code) ? (double?)null : code).Should().Equal(expected);
    }
}
