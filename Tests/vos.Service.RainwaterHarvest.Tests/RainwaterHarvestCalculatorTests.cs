using FluentAssertions;
using vos.Service.RainwaterHarvest.Services;
using Xunit;

namespace vos.Service.RainwaterHarvest.Tests;

// The arithmetic on its own, with no model behind it. Every expected value is worked out here rather
// than copied from the implementation.
public class RainwaterHarvestCalculatorTests
{
    // LAND_INTAKE.md's worked example: Willow Bend's 8.88 ha of hard surface under 700 mm of rain at a
    // runoff coefficient of 0.8, against 320 residents at the 55 m³ a year the shared study archetype
    // declares and 8.16 ha of growing land at 5,000 m³ per hectare a year.
    private static RainwaterHarvestInputs WillowBend => new(
        BuiltFootprintHectares: 8.88,
        RainfallMillimetresPerYear: 700,
        RunoffCoefficient: 0.8,
        PopulationResidents: 320,
        PerCapitaConsumptionM3PerYear: 55,
        ProductiveFootprintHectares: 8.16,
        IrrigationDemandM3PerHectarePerYear: 5000);

    [Fact]
    public void The_hard_surface_the_rainfall_and_the_runoff_give_the_volume_captured_in_a_year()
    {
        var result = RainwaterHarvestCalculator.Compute(WillowBend);

        result.HarvestM3PerYear.Should().BeApproximately(49728, 1e-6);
    }

    [Fact]
    public void Each_demand_component_is_reported_beside_the_total_it_sums_to()
    {
        var result = RainwaterHarvestCalculator.Compute(WillowBend);

        result.DomesticDemandM3PerYear.Should().BeApproximately(17600, 1e-9);
        result.IrrigationDemandM3PerYear.Should().BeApproximately(40800, 1e-9);
        result.TotalWaterDemandM3PerYear.Should().BeApproximately(58400, 1e-9);
    }

    [Fact]
    public void The_percentage_measures_the_harvest_against_the_whole_demand()
    {
        var result = RainwaterHarvestCalculator.Compute(WillowBend);

        result.PctOfWaterDemand.Should().BeApproximately(85.150684931, 1e-9);
    }

    // The reason the components are separate outputs rather than one total. Willow Bend's harvest covers
    // drinking water nearly three times over and falls short of irrigation, and the single 85% says
    // neither — a site uniformly short of both would report the same figure and call for the opposite
    // decision.
    [Fact]
    public void The_components_tell_a_comfortable_domestic_position_from_a_short_irrigation_one()
    {
        var result = RainwaterHarvestCalculator.Compute(WillowBend);

        (result.HarvestM3PerYear / result.DomesticDemandM3PerYear * 100).Should().BeApproximately(282.545454545, 1e-9);
        (result.HarvestM3PerYear / result.IrrigationDemandM3PerYear * 100).Should().BeApproximately(121.882352941, 1e-9);
    }

    [Fact]
    public void A_year_without_rain_harvests_nothing_and_meets_none_of_the_demand()
    {
        var result = RainwaterHarvestCalculator.Compute(WillowBend with { RainfallMillimetresPerYear = 0 });

        result.HarvestM3PerYear.Should().Be(0);
        result.PctOfWaterDemand.Should().Be(0);
    }

    // Nobody drinking and nothing growing is a site with no demand, not a division by zero. Reported as
    // zero rather than as the infinity or the not-a-number the division would produce, either of which a
    // range would read as a verdict.
    [Fact]
    public void A_site_with_nobody_to_supply_and_nothing_to_irrigate_has_no_demand_and_no_shortfall()
    {
        var result = RainwaterHarvestCalculator.Compute(
            WillowBend with { PopulationResidents = 0, ProductiveFootprintHectares = 0 });

        result.DomesticDemandM3PerYear.Should().Be(0);
        result.IrrigationDemandM3PerYear.Should().Be(0);
        result.TotalWaterDemandM3PerYear.Should().Be(0);
        result.PctOfWaterDemand.Should().Be(0);
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
}
