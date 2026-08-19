namespace vos.Service.RainwaterHarvest.Services;

public sealed record RainwaterHarvestInputs(
    double BuiltFootprintHectares,
    double RainfallMillimetresPerYear,
    double RunoffCoefficient,
    double PopulationResidents,
    double PerCapitaConsumptionM3PerYear,
    double ProductiveFootprintHectares,
    double IrrigationDemandM3PerHectarePerYear);

public sealed record RainwaterHarvestOutputs(
    double HarvestM3PerYear,
    double DomesticDemandM3PerYear,
    double IrrigationDemandM3PerYear,
    double TotalDemandM3PerYear,
    double PctOfWaterDemand);

// How much rain the hard surface can capture in a year, and how far that goes against what the site
// drinks and what it irrigates.
//
// The two demand components are separate outputs, and that is the whole point of the service. Irrigation
// usually dwarfs domestic demand, so a site with abundant drinking water and a marginal irrigation
// position reports the same combined percentage as one that is uniformly short — and the two call for
// opposite decisions. The tool this replaces reported only the combined figure.
//
// A hectare is ten thousand square metres and a millimetre of rain is a thousandth of a metre, so the
// two conversions leave a factor of ten between hectare-millimetres and cubic metres.
public static class RainwaterHarvestCalculator
{
    private const double CubicMetresPerHectareMillimetre = 10.0;

    public static RainwaterHarvestOutputs Compute(RainwaterHarvestInputs input)
    {
        var harvest = input.BuiltFootprintHectares * input.RainfallMillimetresPerYear
                      * input.RunoffCoefficient * CubicMetresPerHectareMillimetre;

        var domesticDemand = input.PopulationResidents * input.PerCapitaConsumptionM3PerYear;
        var irrigationDemand = input.ProductiveFootprintHectares * input.IrrigationDemandM3PerHectarePerYear;
        var totalDemand = domesticDemand + irrigationDemand;

        // A site that drinks nothing and irrigates nothing is not short of water, which is a share of
        // nothing rather than a division.
        var pctOfWaterDemand = totalDemand > 0 ? harvest / totalDemand * 100.0 : 0.0;

        return new RainwaterHarvestOutputs(
            harvest, domesticDemand, irrigationDemand, totalDemand, pctOfWaterDemand);
    }
}
