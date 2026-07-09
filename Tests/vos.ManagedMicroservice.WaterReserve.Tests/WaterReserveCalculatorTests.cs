using FluentAssertions;
using vos.ManagedMicroservice.WaterReserve.Services;
using Xunit;

namespace vos.ManagedMicroservice.WaterReserve.Tests;

public class WaterReserveCalculatorTests
{
    [Fact]
    public void Computes_reserve_consumption_pct_and_days()
    {
        var r = WaterReserveCalculator.Compute(new WaterReserveInputs(
            PopulationResidents: 1000, PerCapitaConsumptionM3PerYear: 50, StorageCapacityM3: 100_000));

        r.AnnualConsumptionM3.Should().Be(50_000);
        r.EmergencyReserveM3.Should().Be(100_000);
        r.PctAnnualConsumption.Should().BeApproximately(200.0, 1e-9);   // reserve is 2x annual use
        r.DaysOfSupply.Should().BeApproximately(730.0, 1e-6);           // 100000 / (50000/365)
    }

    [Fact]
    public void Days_of_supply_hits_the_14_day_target_at_the_right_reserve()
    {
        const double pop = 730, perCapita = 50;
        var annual = pop * perCapita;                 // 36,500 m3/yr
        var fourteenDays = 14.0 * (annual / 365.0);   // stored volume for exactly 14 days

        var r = WaterReserveCalculator.Compute(new WaterReserveInputs(pop, perCapita, fourteenDays));

        r.DaysOfSupply.Should().BeApproximately(14.0, 1e-9);
    }

    [Fact]
    public void Zero_population_does_not_divide_by_zero()
    {
        var r = WaterReserveCalculator.Compute(new WaterReserveInputs(0, 50, 100_000));

        r.AnnualConsumptionM3.Should().Be(0);
        r.PctAnnualConsumption.Should().Be(0);
        r.DaysOfSupply.Should().Be(0);
    }
}
