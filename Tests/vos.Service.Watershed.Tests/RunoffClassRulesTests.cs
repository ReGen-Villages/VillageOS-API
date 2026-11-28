using FluentAssertions;
using vos.Service.Watershed.Terrain;
using Xunit;

namespace vos.Service.Watershed.Tests;

// Made-up classes, so the rule is shown apart from the classes any model ships.
public class RunoffClassRulesTests
{
    private static RunoffCellMeasures Cell(
        double hollow = 0, double standing = 0, double share = 0, double catchment = 1, double depth = 0, double depthTimesVelocity = 0) =>
        new(hollow, standing, share, catchment, depth, depthTimesVelocity);

    private static RunoffClassRule Class(long order, long gridCode, Dictionary<string, double> conditions,
        params Dictionary<string, double>[] alternatives) => new(order, gridCode, conditions, [.. alternatives]);

    private static readonly RunoffClassRule Rest = Class(1, 10, new());

    [Fact]
    public void A_cell_takes_the_highest_order_class_whose_conditions_all_hold()
    {
        var wide = Class(2, 20, new() { ["catchmentAboveCells"] = 2 });
        var wideAndShared = Class(3, 30, new() { ["catchmentAboveCells"] = 2, ["shareOfLargestFlowAtLeast"] = 0.5 });

        RunoffClassRules.ClassOf([Rest, wideAndShared, wide], Cell(catchment: 5, share: 0.6))!.GridCode.Should().Be(30);
        RunoffClassRules.ClassOf([Rest, wideAndShared, wide], Cell(catchment: 5, share: 0.4))!.GridCode.Should().Be(20);
        RunoffClassRules.ClassOf([Rest, wideAndShared, wide], Cell(catchment: 2, share: 0.6))!.GridCode.Should().Be(10);
    }

    [Theory]
    [InlineData(0.5, 0.0, true)]
    [InlineData(0.0, 0.5, true)]
    [InlineData(0.5, 0.5, true)]
    [InlineData(0.0, 0.0, false)]
    public void A_class_with_alternatives_needs_its_own_conditions_and_all_of_at_least_one_alternative(
        double depth, double depthTimesVelocity, bool takesIt)
    {
        var either = Class(2, 20, new() { ["catchmentAboveCells"] = 2 },
            new() { ["flowDepthAboveMetres"] = 0.3 }, new() { ["depthTimesVelocityAboveSquareMetresPerSecond"] = 0.3 });

        RunoffClassRules.ClassOf([Rest, either], Cell(catchment: 5, depth: depth, depthTimesVelocity: depthTimesVelocity))!.GridCode
            .Should().Be(takesIt ? 20 : 10);
        RunoffClassRules.ClassOf([Rest, either], Cell(catchment: 1, depth: 0.5, depthTimesVelocity: 0.5))!.GridCode.Should().Be(10);
    }

    [Fact]
    public void A_corrected_condition_moves_a_cell()
    {
        var cell = Cell(hollow: 0.6, standing: 0.1);

        RunoffClassRules.ClassOf([Rest, Class(2, 20, new() { ["hollowAtLeastMetres"] = 0.5 })], cell)!.GridCode.Should().Be(20);
        RunoffClassRules.ClassOf([Rest, Class(2, 20, new() { ["hollowAtLeastMetres"] = 0.8 })], cell)!.GridCode.Should().Be(10);
        RunoffClassRules.ClassOf([Rest, Class(2, 20, new(), new Dictionary<string, double> { ["standingWaterAtLeastMetres"] = 0.2 })], cell)!
            .GridCode.Should().Be(10);
    }

    [Theory]
    [InlineData("hollowAtLeastMetres", 0.5, 0.5, true)]
    [InlineData("standingWaterAtLeastMetres", 0.05, 0.05, true)]
    [InlineData("shareOfLargestFlowAtLeast", 0.05, 0.05, true)]
    [InlineData("catchmentAboveCells", 1.5, 1.5, false)]
    [InlineData("flowDepthAboveMetres", 0.3, 0.3, false)]
    [InlineData("depthTimesVelocityAboveSquareMetresPerSecond", 0.3, 0.3, false)]
    [InlineData("catchmentAboveCells", 1.5, 1.6, true)]
    [InlineData("flowDepthAboveMetres", 0.3, 0.31, true)]
    [InlineData("depthTimesVelocityAboveSquareMetresPerSecond", 0.3, 0.31, true)]
    public void An_at_least_condition_holds_at_its_value_and_an_above_condition_only_past_it(
        string condition, double threshold, double measured, bool holds)
    {
        var cell = condition switch
        {
            "hollowAtLeastMetres" => Cell(hollow: measured),
            "standingWaterAtLeastMetres" => Cell(standing: measured),
            "shareOfLargestFlowAtLeast" => Cell(share: measured),
            "catchmentAboveCells" => Cell(catchment: measured),
            "flowDepthAboveMetres" => Cell(depth: measured),
            _ => Cell(depthTimesVelocity: measured),
        };

        RunoffClassRules.ClassOf([Rest, Class(2, 20, new() { [condition] = threshold })], cell)!.GridCode.Should().Be(holds ? 20 : 10);
    }

    [Fact]
    public void A_class_with_no_conditions_and_no_alternatives_takes_every_cell_no_other_class_takes() =>
        RunoffClassRules.ClassOf([Rest, Class(2, 20, new() { ["hollowAtLeastMetres"] = 0.5 })], Cell())!.GridCode.Should().Be(10);

    [Fact]
    public void A_condition_the_reader_does_not_know_never_holds() =>
        RunoffClassRules.ClassOf([Rest, Class(2, 20, new() { ["somethingElse"] = 0 })], Cell(hollow: 9))!.GridCode.Should().Be(10);

    [Fact]
    public void With_no_class_that_takes_the_rest_a_cell_meeting_none_has_no_class() =>
        RunoffClassRules.ClassOf([Class(2, 20, new() { ["hollowAtLeastMetres"] = 0.5 })], Cell()).Should().BeNull();
}
