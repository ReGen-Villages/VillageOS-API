namespace vos.ManagedMicroservice.WaterReserve.Services;

public sealed record WaterReserveInputs(
    double PopulationResidents,
    double PerCapitaConsumptionM3PerYear,
    double StorageCapacityM3);

public sealed record WaterReserveOutputs(
    double EmergencyReserveM3,
    double AnnualConsumptionM3,
    double PctAnnualConsumption,
    double DaysOfSupply);

// Emergency water reserve under district failure: stored water measured against consumption.
// Scale-independent — population and rate are inputs — so the programme-scale decision only feeds data,
// never the code. DaysOfSupply feeds the 14-day "WaterResilient" range.
public static class WaterReserveCalculator
{
    public static WaterReserveOutputs Compute(WaterReserveInputs input)
    {
        var annualConsumption = input.PopulationResidents * input.PerCapitaConsumptionM3PerYear;
        var reserve = input.StorageCapacityM3;

        // No consumers => the reserve covers indefinitely; report 0 rather than dividing by zero.
        var pct = annualConsumption > 0 ? reserve / annualConsumption * 100.0 : 0.0;
        var days = annualConsumption > 0 ? reserve / (annualConsumption / 365.0) : 0.0;

        return new WaterReserveOutputs(reserve, annualConsumption, pct, days);
    }
}
