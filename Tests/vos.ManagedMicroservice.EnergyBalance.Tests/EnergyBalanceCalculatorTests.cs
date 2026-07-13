using FluentAssertions;
using vos.ManagedMicroservice.EnergyBalance.Services;
using Xunit;

namespace vos.ManagedMicroservice.EnergyBalance.Tests;

public class EnergyBalanceCalculatorTests
{
    [Fact]
    public void Pv_geometry_reproduces_the_case_study_solar_figure()
    {
        // The ingested IFC's real PV area x the site's own solar resource x ~20% efficiency reproduces
        // the case study's ~13,500 MWh/yr solar figure from first principles (design doc §4).
        var r = EnergyBalanceCalculator.Compute(new EnergyBalanceInputs(
            SolarPvAreaM2: 29_611, SolarResourceKwhPerM2PerYear: 2279.5, PvEfficiency: 0.20,
            OtherGenerationMwhPerYear: 0, AnnualConsumptionMwhPerYear: 1));

        r.SolarGenerationMwhPerYear.Should().BeApproximately(13_500, 5);
    }

    [Fact]
    public void Total_generation_adds_other_sources_and_reports_pct_of_consumption()
    {
        var r = EnergyBalanceCalculator.Compute(new EnergyBalanceInputs(
            SolarPvAreaM2: 100_000, SolarResourceKwhPerM2PerYear: 1000, PvEfficiency: 0.20, // 20,000 MWh solar
            OtherGenerationMwhPerYear: 900,                                                 // + 900 => 20,900
            AnnualConsumptionMwhPerYear: 18_743));                                          // case study demand

        r.SolarGenerationMwhPerYear.Should().BeApproximately(20_000, 1e-6);
        r.TotalGenerationMwhPerYear.Should().BeApproximately(20_900, 1e-6);
        r.PctOfConsumption.Should().BeApproximately(20_900.0 / 18_743.0 * 100.0, 1e-6);     // ~111.5%
        r.NetPositive.Should().BeTrue();
    }

    [Fact]
    public void Not_net_positive_when_generation_is_short()
    {
        var r = EnergyBalanceCalculator.Compute(new EnergyBalanceInputs(
            SolarPvAreaM2: 10_000, SolarResourceKwhPerM2PerYear: 1000, PvEfficiency: 0.20, // 2,000 MWh
            OtherGenerationMwhPerYear: 0, AnnualConsumptionMwhPerYear: 5_000));

        r.PctOfConsumption.Should().BeApproximately(40.0, 1e-6);
        r.NetPositive.Should().BeFalse();
    }

    [Fact]
    public void Zero_consumption_does_not_divide_by_zero()
    {
        var r = EnergyBalanceCalculator.Compute(new EnergyBalanceInputs(1000, 1000, 0.2, 0, 0));

        r.PctOfConsumption.Should().Be(0);
        r.NetPositive.Should().BeFalse();
    }
}
