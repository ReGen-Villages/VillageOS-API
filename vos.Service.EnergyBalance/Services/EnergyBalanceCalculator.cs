namespace vos.Service.EnergyBalance.Services;

public sealed record EnergyBalanceInputs(
    double SolarPvAreaM2,
    double SolarResourceKwhPerM2PerYear,   // GTI at optimum tilt
    double ModuleEfficiency,               // 0..1, measured at the panel under standard test conditions
    double PerformanceRatio,               // 0..1, everything lost between the panel and the meter
    double OtherGenerationMwhPerYear,      // firm/dispatchable: wind + biomass + biogas
    double AnnualConsumptionMwhPerYear);

public sealed record EnergyBalanceOutputs(
    double SolarGenerationMwhPerYear,
    double TotalGenerationMwhPerYear,
    double PctOfConsumption,
    bool NetPositive);

// Site energy balance: solar plus other sources, measured against annual consumption. Net-positive when
// generation covers consumption. Scale-independent — areas and consumption are inputs. PctOfConsumption
// feeds the EnergyNetPositive range.
//
// Solar takes both factors rather than one combined efficiency because a single port accepts a module
// efficiency without complaint, which overstates output by roughly the performance ratio.
public static class EnergyBalanceCalculator
{
    public static EnergyBalanceOutputs Compute(EnergyBalanceInputs input)
    {
        var systemYieldFactor = input.ModuleEfficiency * input.PerformanceRatio;
        var solarMwh = input.SolarPvAreaM2 * input.SolarResourceKwhPerM2PerYear * systemYieldFactor / 1000.0;
        var totalGeneration = solarMwh + input.OtherGenerationMwhPerYear;

        var pct = input.AnnualConsumptionMwhPerYear > 0
            ? totalGeneration / input.AnnualConsumptionMwhPerYear * 100.0
            : 0.0;
        var netPositive = input.AnnualConsumptionMwhPerYear > 0
            && totalGeneration >= input.AnnualConsumptionMwhPerYear;

        return new EnergyBalanceOutputs(solarMwh, totalGeneration, pct, netPositive);
    }
}
