namespace vos.Service.WaterReserve.Services;

public sealed record WaterReserveInputs(
    double PopulationResidents,
    double PerCapitaConsumptionM3PerYear,
    double StorageCapacityM3);

public sealed record WaterReserveOutputs(
    double EmergencyReserveM3,
    double DomesticConsumptionM3PerYear,
    double PctAnnualConsumption,
    double DaysOfSupply);

// Emergency water reserve under district failure: stored water measured against what the residents
// drink. That is domestic consumption alone and not everything the site uses — a site that irrigates
// wants far more — which is right for a reserve, because a reserve is drinking water.
// Scale-independent — population and rate are inputs — so the programme-scale decision only feeds data,
// never the code. DaysOfSupply feeds the 14-day "WaterResilient" range.
public static class WaterReserveCalculator
{
    public static WaterReserveOutputs Compute(WaterReserveInputs input)
    {
        var drunkInAYear = input.PopulationResidents * input.PerCapitaConsumptionM3PerYear;
        var reserve = input.StorageCapacityM3;

        // No consumers => the reserve covers indefinitely; report 0 rather than dividing by zero.
        var pct = drunkInAYear > 0 ? reserve / drunkInAYear * 100.0 : 0.0;
        var days = drunkInAYear > 0 ? reserve / (drunkInAYear / 365.0) : 0.0;

        return new WaterReserveOutputs(reserve, drunkInAYear, pct, days);
    }
}
