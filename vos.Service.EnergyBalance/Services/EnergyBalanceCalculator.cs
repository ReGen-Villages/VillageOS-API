namespace vos.Service.EnergyBalance.Services;

public sealed record EnergyBalanceInputs(
    double SolarPvAreaM2,
    double SolarResourceKwhPerM2PerYear,   // GTI at optimum tilt
    double PvEfficiency,                   // 0..1
    double OtherGenerationMwhPerYear,      // firm/dispatchable: wind + biomass + biogas
    double AnnualConsumptionMwhPerYear);

public sealed record EnergyBalanceOutputs(
    double SolarGenerationMwhPerYear,
    double TotalGenerationMwhPerYear,
    double PctOfConsumption,
    bool NetPositive);

// Site energy balance: solar (PV area x resource x efficiency) plus other sources, measured against
// annual consumption. Net-positive when generation covers consumption. Scale-independent — areas and
// consumption are inputs. PctOfConsumption feeds the EnergyNetPositive range.
public static class EnergyBalanceCalculator
{
    public static EnergyBalanceOutputs Compute(EnergyBalanceInputs input)
    {
        var solarMwh = input.SolarPvAreaM2 * input.SolarResourceKwhPerM2PerYear * input.PvEfficiency / 1000.0;
        var totalGeneration = solarMwh + input.OtherGenerationMwhPerYear;

        var pct = input.AnnualConsumptionMwhPerYear > 0
            ? totalGeneration / input.AnnualConsumptionMwhPerYear * 100.0
            : 0.0;
        var netPositive = input.AnnualConsumptionMwhPerYear > 0
            && totalGeneration >= input.AnnualConsumptionMwhPerYear;

        return new EnergyBalanceOutputs(solarMwh, totalGeneration, pct, netPositive);
    }
}
