using FluentAssertions;
using vos.Service.EnergyBalance.Services;
using Xunit;

namespace vos.Service.EnergyBalance.Tests;

public class EnergyBalanceCalculatorTests
{
    private const double ModuleEfficiency = 0.17;
    private const double PerformanceRatio = 0.77;

    [Fact]
    public void Solar_output_applies_module_efficiency_and_performance_ratio_together()
    {
        var r = EnergyBalanceCalculator.Compute(new EnergyBalanceInputs(
            SolarPvAreaM2: 10_000, SolarResourceKwhPerM2PerYear: 1_000,
            ModuleEfficiency: ModuleEfficiency, PerformanceRatio: PerformanceRatio,
            OtherGenerationMwhPerYear: 0, AnnualConsumptionMwhPerYear: 1));

        r.SolarGenerationMwhPerYear.Should().BeApproximately(10_000 * 1_000 * 0.1309 / 1000.0, 1e-6);
    }

    [Fact]
    public void Module_efficiency_alone_would_overstate_output_by_the_performance_ratio()
    {
        var inputs = new EnergyBalanceInputs(
            SolarPvAreaM2: 10_000, SolarResourceKwhPerM2PerYear: 1_000,
            ModuleEfficiency: ModuleEfficiency, PerformanceRatio: PerformanceRatio,
            OtherGenerationMwhPerYear: 0, AnnualConsumptionMwhPerYear: 1);

        var withLosses = EnergyBalanceCalculator.Compute(inputs);
        var withoutLosses = EnergyBalanceCalculator.Compute(inputs with { PerformanceRatio = 1.0 });

        withLosses.SolarGenerationMwhPerYear
            .Should().BeApproximately(withoutLosses.SolarGenerationMwhPerYear * PerformanceRatio, 1e-6);
    }

    [Fact]
    public void Case_study_geometry_yields_less_than_the_design_document_claimed()
    {
        // The design document put this site at ~13,500 MWh/yr, computed from module efficiency alone.
        // The same geometry through the delivered yield factor is about a third lower.
        var r = EnergyBalanceCalculator.Compute(new EnergyBalanceInputs(
            SolarPvAreaM2: 29_611, SolarResourceKwhPerM2PerYear: 2279.5,
            ModuleEfficiency: ModuleEfficiency, PerformanceRatio: PerformanceRatio,
            OtherGenerationMwhPerYear: 0, AnnualConsumptionMwhPerYear: 1));

        r.SolarGenerationMwhPerYear.Should().BeApproximately(8_836, 5);
    }

    // The two tests below exercise totalling and the net-positive threshold, not the yield factor, so they
    // use a lossless performance ratio to keep the solar figure round. One is not a plausible site value.
    [Fact]
    public void Total_generation_adds_other_sources_and_reports_pct_of_consumption()
    {
        var r = EnergyBalanceCalculator.Compute(new EnergyBalanceInputs(
            SolarPvAreaM2: 100_000, SolarResourceKwhPerM2PerYear: 1000,
            ModuleEfficiency: 0.20, PerformanceRatio: 1.0,   // 20,000 MWh solar
            OtherGenerationMwhPerYear: 900,                  // + 900 => 20,900
            AnnualConsumptionMwhPerYear: 18_743));           // case study demand

        r.SolarGenerationMwhPerYear.Should().BeApproximately(20_000, 1e-6);
        r.TotalGenerationMwhPerYear.Should().BeApproximately(20_900, 1e-6);
        r.PctOfConsumption.Should().BeApproximately(20_900.0 / 18_743.0 * 100.0, 1e-6);     // ~111.5%
        r.NetPositive.Should().BeTrue();
    }

    [Fact]
    public void Not_net_positive_when_generation_is_short()
    {
        var r = EnergyBalanceCalculator.Compute(new EnergyBalanceInputs(
            SolarPvAreaM2: 10_000, SolarResourceKwhPerM2PerYear: 1000,
            ModuleEfficiency: 0.20, PerformanceRatio: 1.0,   // 2,000 MWh
            OtherGenerationMwhPerYear: 0, AnnualConsumptionMwhPerYear: 5_000));

        r.PctOfConsumption.Should().BeApproximately(40.0, 1e-6);
        r.NetPositive.Should().BeFalse();
    }

    [Fact]
    public void Zero_consumption_does_not_divide_by_zero()
    {
        var r = EnergyBalanceCalculator.Compute(new EnergyBalanceInputs(1000, 1000, 0.2, 1.0, 0, 0));

        r.PctOfConsumption.Should().Be(0);
        r.NetPositive.Should().BeFalse();
    }
}
