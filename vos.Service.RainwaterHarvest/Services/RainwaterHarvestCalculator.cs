namespace vos.Service.RainwaterHarvest.Services;

/// <summary>One demand the harvest has to serve, and how much water it wants in a year. The size is the
/// model's own arithmetic — a formula on the study, read off it before this is called — as is which
/// demands there are and the order they arrive in.</summary>
public sealed record DemandToServe(string Name, double SizeM3PerYear);

/// <summary>What one demand asked for, how much of it the harvest reached, and what is left uncovered.
/// A shortfall is zero when the demand is met and never negative — a surplus is read off the harvest
/// against the whole demand, not off a component that has already been filled.</summary>
public sealed record ServedDemand(
    string Name, double DemandM3PerYear, double PctCovered, double ShortfallM3PerYear);

public sealed record RainwaterHarvestInputs(
    double BuiltFootprintHectares,
    double RainfallMillimetresPerYear,
    double RunoffCoefficient,
    IReadOnlyList<DemandToServe> Demands);

public sealed record RainwaterHarvestOutputs(
    double HarvestM3PerYear,
    double TotalWaterDemandM3PerYear,
    double PctOfWaterDemand,
    IReadOnlyList<ServedDemand> Served);

// How much rain the hard surface can capture in a year, and how far that goes against each demand the
// site puts on it.
//
// The harvest is one body of water. Measuring it against each demand on its own would count the same
// cubic metre once per demand — Willow Bend would read 282% of its drinking water and 122% of its
// irrigation while holding less than either pair of figures claims. So the demands are served in turn:
// each takes what it needs from what is left, and the coverages then describe one volume rather than
// several claims on it.
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

        var served = new List<ServedDemand>(input.Demands.Count);
        var unclaimed = harvest;
        var totalWaterDemand = 0.0;

        foreach (var demand in input.Demands)
        {
            var size = demand.SizeM3PerYear;
            var taken = Math.Min(unclaimed, size);
            unclaimed -= taken;
            totalWaterDemand += size;

            // A demand of nothing is met by nothing, which is a share of nothing rather than a division.
            // Reported as zero rather than as the infinity or the not-a-number the division would give,
            // either of which a range would read as a verdict.
            served.Add(new ServedDemand(demand.Name, size,
                size > 0 ? taken / size * 100.0 : 0.0, size - taken));
        }

        return new RainwaterHarvestOutputs(harvest, totalWaterDemand,
            totalWaterDemand > 0 ? harvest / totalWaterDemand * 100.0 : 0.0, served);
    }
}
