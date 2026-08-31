using FluentAssertions;
using vos.Service.RainwaterHarvest.Services;
using Xunit;

namespace vos.Service.RainwaterHarvest.Tests;

// The arithmetic on its own, with no model behind it. Every expected value is worked out here rather
// than copied from the implementation.
public class RainwaterHarvestCalculatorTests
{
    // LAND_INTAKE.md's worked example: Willow Bend's 8.88 ha of hard surface under 700 mm of rain at a
    // runoff coefficient of 0.8, serving 320 residents at the 55 m³ a year the shared study archetype
    // declares — 17,600 m³ — and 8.16 ha of growing land at 5,000 m³ per hectare a year — 40,800 m³.
    // Those two products are formulas the study declares now, so they arrive here already worked out.
    private static RainwaterHarvestInputs WillowBend => new(
        BuiltFootprintHectares: 8.88,
        RainfallMillimetresPerYear: 700,
        RunoffCoefficient: 0.8,
        Demands:
        [
            new DemandToServe("domestic-demand", SizeM3PerYear: 320 * 55),
            new DemandToServe("irrigation-demand", SizeM3PerYear: 8.16 * 5000),
        ]);

    private static ServedDemand Served(RainwaterHarvestOutputs outputs, string name) =>
        outputs.Served.Single(demand => demand.Name == name);

    [Fact]
    public void The_hard_surface_the_rainfall_and_the_runoff_give_the_volume_captured_in_a_year()
    {
        var result = RainwaterHarvestCalculator.Compute(WillowBend);

        result.HarvestM3PerYear.Should().BeApproximately(49728, 1e-6);
    }

    [Fact]
    public void Each_demand_is_a_quantity_times_a_rate_and_the_total_is_their_sum()
    {
        var result = RainwaterHarvestCalculator.Compute(WillowBend);

        Served(result, "domestic-demand").DemandM3PerYear.Should().BeApproximately(17600, 1e-9);
        Served(result, "irrigation-demand").DemandM3PerYear.Should().BeApproximately(40800, 1e-9);
        result.TotalWaterDemandM3PerYear.Should().BeApproximately(58400, 1e-9);
    }

    [Fact]
    public void The_percentage_measures_the_harvest_against_the_whole_demand()
    {
        var result = RainwaterHarvestCalculator.Compute(WillowBend);

        result.PctOfWaterDemand.Should().BeApproximately(85.150684931, 1e-9);
    }

    // The order is the answer, not a tidying. Willow Bend harvests 49,728 m³ against 17,600 m³ of drinking
    // water and 40,800 m³ of irrigation. Served in order, drinking water takes 17,600 and irrigation takes
    // the 32,128 left — 78.75% of what it wanted, 8,672 m³ short. Worked out against each demand on its
    // own it would read 282.55% and 121.88%, which between them claim nearly four times the water there is.
    [Fact]
    public void Drinking_water_is_served_first_and_irrigation_takes_what_is_left()
    {
        var result = RainwaterHarvestCalculator.Compute(WillowBend);

        var domestic = Served(result, "domestic-demand");
        domestic.PctCovered.Should().BeApproximately(100, 1e-9);
        domestic.ShortfallM3PerYear.Should().Be(0);

        var irrigation = Served(result, "irrigation-demand");
        irrigation.PctCovered.Should().BeApproximately(78.7450980392, 1e-9);
        irrigation.ShortfallM3PerYear.Should().BeApproximately(8672, 1e-9);
    }

    // 8.88 ha under 200 mm captures 14,208 m³, which does not reach the 17,600 m³ drunk. Nothing is left
    // for irrigation, so it covers none of its 40,800 m³ — and both shortfalls are stated, because a
    // planner deciding where the next cubic metre goes needs the size of each gap, not only the worse one.
    [Fact]
    public void A_harvest_short_of_drinking_water_leaves_irrigation_nothing()
    {
        var result = RainwaterHarvestCalculator.Compute(WillowBend with { RainfallMillimetresPerYear = 200 });

        var domestic = Served(result, "domestic-demand");
        domestic.PctCovered.Should().BeApproximately(80.7272727273, 1e-9);
        domestic.ShortfallM3PerYear.Should().BeApproximately(3392, 1e-9);

        var irrigation = Served(result, "irrigation-demand");
        irrigation.PctCovered.Should().Be(0);
        irrigation.ShortfallM3PerYear.Should().BeApproximately(40800, 1e-9);
    }

    // 71,040 m³ against 58,400 m³ of demand. Coverage stops at full rather than reporting the surplus:
    // what is left over is read off the harvest against the total, which is the figure that carries it.
    [Fact]
    public void A_harvest_larger_than_the_whole_demand_covers_every_component_and_leaves_no_shortfall()
    {
        var result = RainwaterHarvestCalculator.Compute(WillowBend with { RainfallMillimetresPerYear = 1000 });

        result.Served.Should().OnlyContain(demand => demand.PctCovered == 100 && demand.ShortfallM3PerYear == 0);
        result.PctOfWaterDemand.Should().BeApproximately(121.6438356164, 1e-9);
    }

    // A site with nobody to supply and nothing to irrigate is not short of water: each component is a
    // share of nothing rather than a division, reported as zero the same way the combined percentage is.
    // The infinity or the not-a-number a division would give is what a range would read as a verdict.
    [Fact]
    public void A_component_with_no_demand_is_a_share_of_nothing_rather_than_a_division()
    {
        var result = RainwaterHarvestCalculator.Compute(WillowBend with
        {
            Demands =
            [
                new DemandToServe("domestic-demand", SizeM3PerYear: 0),
                new DemandToServe("irrigation-demand", SizeM3PerYear: 0),
            ],
        });

        result.Served.Should().OnlyContain(demand =>
            demand.DemandM3PerYear == 0 && demand.PctCovered == 0 && demand.ShortfallM3PerYear == 0);
        result.TotalWaterDemandM3PerYear.Should().Be(0);
        result.PctOfWaterDemand.Should().Be(0);
    }

    // The whole reason the coverages are apportioned rather than worked out one at a time. Whatever the
    // harvest and the demands, what the components report as covered cannot add up to more water than was
    // captured — which is the claim a reader of two percentages is entitled to make.
    [Theory]
    [InlineData(0)]
    [InlineData(200)]
    [InlineData(700)]
    [InlineData(1000)]
    public void The_components_never_report_more_water_covered_than_the_harvest_holds(double rainfall)
    {
        var result = RainwaterHarvestCalculator.Compute(
            WillowBend with { RainfallMillimetresPerYear = rainfall });

        var covered = result.Served.Sum(demand => demand.DemandM3PerYear * demand.PctCovered / 100.0);

        covered.Should().BeLessThanOrEqualTo(result.HarvestM3PerYear + 1e-9);
    }

    [Fact]
    public void A_year_without_rain_harvests_nothing_and_meets_none_of_the_demand()
    {
        var result = RainwaterHarvestCalculator.Compute(WillowBend with { RainfallMillimetresPerYear = 0 });

        result.HarvestM3PerYear.Should().Be(0);
        result.PctOfWaterDemand.Should().Be(0);
        result.Served.Should().OnlyContain(demand => demand.PctCovered == 0);
    }

    [Fact]
    public void A_site_whose_land_is_all_soft_captures_nothing_however_hard_it_rains()
    {
        var result = RainwaterHarvestCalculator.Compute(WillowBend with { BuiltFootprintHectares = 0 });

        result.HarvestM3PerYear.Should().Be(0);
        result.TotalWaterDemandM3PerYear.Should().BeApproximately(58400, 1e-9);
        result.PctOfWaterDemand.Should().Be(0);
    }

    // A runoff coefficient is the fraction of rain a surface actually yields, so it scales the harvest and
    // nothing else. Half the coefficient, half the volume, the same demand.
    [Fact]
    public void The_runoff_coefficient_scales_the_harvest_and_leaves_the_demand_where_it_was()
    {
        var result = RainwaterHarvestCalculator.Compute(WillowBend with { RunoffCoefficient = 0.4 });

        result.HarvestM3PerYear.Should().BeApproximately(24864, 1e-6);
        result.TotalWaterDemandM3PerYear.Should().BeApproximately(58400, 1e-9);
    }

    // The order is the model's, not the order the components happened to arrive in. A reader handed them
    // the other way round would otherwise water the crop before the village drinks.
    [Fact]
    public void The_demands_are_served_in_the_order_they_are_given()
    {
        var reversed = RainwaterHarvestCalculator.Compute(WillowBend with
        {
            Demands = [.. WillowBend.Demands.Reverse()],
        });

        Served(reversed, "irrigation-demand").PctCovered.Should().BeApproximately(100, 1e-9);
        Served(reversed, "domestic-demand").PctCovered.Should().BeApproximately(50.7272727273, 1e-9);
    }
}
