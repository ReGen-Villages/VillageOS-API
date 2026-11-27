using FluentAssertions;
using vos.Service.Watershed.Terrain;
using Xunit;

namespace vos.Service.Watershed.Tests;

// The rain is the same at every duration, so a class depends on the flow alone. The valley falls 2 % along its floor
// and 10 % up each side; its outlet cell gathers about 420 cells, so its flow is about 0.105 × C × i cubic metres a
// second, and it is a flow path once depth × velocity, √Q / 4.75 for a channel narrower than a cell, passes 0.3.
public class StormRunoffTests
{
    private const int Side = 21;
    private const double Cell = 30;
    private const int Middle = Side / 2;

    private static readonly RunoffInputs Inputs = new(
        OpenGroundCoefficient: 0.2, RoofAndPavingCoefficient: 0.85, PlantedGroundCoefficient: 0.15, ImperviousShareOfBuiltGround: 0.6,
        SheetFlowManningRoughness: 0.05, ChannelManningRoughness: 0.05, ShortestTimeOfConcentrationMinutes: 10,
        SheetFlowLengthMetres: 130, SmallestOverlandSlope: 0.002, SmallestChannelSlope: 0.003);

    private static readonly RunoffClassRule NoRunoff = new(1, 0, new Dictionary<string, double>(), []);
    private static readonly RunoffClassRule Shallow = new(2, 1, Concentrated(), []);
    private static readonly RunoffClassRule FlowPath = new(3, 2, Concentrated(),
        [new Dictionary<string, double> { ["flowDepthAboveMetres"] = 0.3 }, new Dictionary<string, double> { ["depthTimesVelocityAboveSquareMetresPerSecond"] = 0.3 }]);
    private static readonly RunoffClassRule StandingWater = new(4, 3,
        new Dictionary<string, double> { ["hollowAtLeastMetres"] = 0.5, ["standingWaterAtLeastMetres"] = 0.05 }, []);
    private static readonly IReadOnlyList<RunoffClassRule> Classes = [NoRunoff, Shallow, FlowPath, StandingWater];

    private static Dictionary<string, double> Concentrated() => new() { ["shareOfLargestFlowAtLeast"] = 0.05, ["catchmentAboveCells"] = 1.5 };

    private static int At(int column, int row) => row * Side + column;

    private static bool Interior(int cell) => cell % Side is > 0 and < Side - 1 && cell / Side is > 0 and < Side - 1;

    private static TerrainSurface Surface(Func<int, int, double> height) => new(
        Side, Side, Cell,
        [.. Enumerable.Range(0, Side * Side).Select(cell => height(cell % Side, cell / Side))],
        [.. Enumerable.Range(0, Side * Side).Select(Interior)]);

    private static DesignStormRain Rain(double millimetresPerHour, double multiplier = 1.25) =>
        new(multiplier, new RainfallIntensities([new RainfallIntensity(5, millimetresPerHour), new RainfallIntensity(1440, millimetresPerHour)]));

    private static IReadOnlyList<double> GridCodes(TerrainSurface surface, DesignStormRain storm, bool builtOver = false, RunoffInputs? inputs = null) =>
        StormRunoff.GridCodesOf(surface, TerrainAnalysis.Of(surface), inputs ?? Inputs, storm, builtOver, Classes);

    private static TerrainSurface Bowl() => Surface((column, row) => 0.1 * Cell * Math.Sqrt(Math.Pow(column - Middle, 2) + Math.Pow(row - Middle, 2)));

    private static TerrainSurface Valley() => Surface((column, row) => 0.1 * Cell * Math.Abs(column - Middle) + 0.02 * Cell * row);

    [Fact]
    public void A_bowl_holds_standing_water_in_its_middle_and_a_cell_outside_the_border_has_no_class()
    {
        var codes = GridCodes(Bowl(), Rain(100));

        codes[At(Middle, Middle)].Should().Be(3);
        Enumerable.Range(0, Side * Side).Where(cell => !Interior(cell)).Should().OnlyContain(cell => double.IsNaN(codes[cell]));
    }

    [Fact]
    public void A_valley_s_floor_near_its_outlet_is_a_flow_path_and_its_sides_are_not()
    {
        var codes = GridCodes(Valley(), Rain(200));

        codes[At(Middle, 1)].Should().Be(2);
        Enumerable.Range(0, Side * Side).Where(cell => Interior(cell) && Math.Abs(cell % Side - Middle) >= 3)
            .Should().OnlyContain(cell => codes[cell] != 2);
        codes.Where(code => !double.IsNaN(code)).Should().NotContain(3);
    }

    [Fact]
    public void A_gentle_plane_in_light_rain_has_only_shallow_runoff_or_none()
    {
        var codes = GridCodes(Surface((column, _) => 0.01 * Cell * column), Rain(1));

        codes.Where(code => !double.IsNaN(code)).Should().OnlyContain(code => code == 0 || code == 1);
    }

    [Fact]
    public void A_runoff_coefficient_raised_by_the_storm_past_one_is_held_at_one()
    {
        var atOne = GridCodes(Valley(), Rain(15, multiplier: 1), inputs: Inputs with { OpenGroundCoefficient = 1 });

        GridCodes(Valley(), Rain(15, multiplier: 1.8), inputs: Inputs with { OpenGroundCoefficient = 1 }).Should().Equal(atOne);
        GridCodes(Valley(), Rain(15, multiplier: 2), inputs: Inputs with { OpenGroundCoefficient = 0.5 }).Should().Equal(atOne);
        atOne[At(Middle, 1)].Should().NotBe(2, "held at one, the outlet's flow stays inside the safe class");
    }

    [Fact]
    public void Ground_built_over_runs_off_more_than_bare_ground()
    {
        GridCodes(Valley(), Rain(50))[At(Middle, 1)].Should().NotBe(2);
        GridCodes(Valley(), Rain(50), builtOver: true)[At(Middle, 1)].Should().Be(2);
    }

    [Fact]
    public void A_corrected_study_input_changes_a_class()
    {
        GridCodes(Valley(), Rain(50), inputs: Inputs with { OpenGroundCoefficient = 0.2 })[At(Middle, 1)].Should().NotBe(2);
        GridCodes(Valley(), Rain(50), inputs: Inputs with { OpenGroundCoefficient = 0.6 })[At(Middle, 1)].Should().Be(2);
    }
}
