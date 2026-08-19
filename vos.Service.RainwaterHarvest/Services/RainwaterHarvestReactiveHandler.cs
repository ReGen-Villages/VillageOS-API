using vos.Service.Shared;

namespace vos.Service.RainwaterHarvest.Services;

// The reactive form, and the only one (#6020): a relationship naming the study makes this read the
// study's effective properties, compute, and write the harvest and its three demand figures back onto
// it as Facts, so the ranges that judge them re-evaluate.
//
// Both footprints are written by LandAllocation onto the same study, which puts this on the second layer
// of the analysis: one dispatch, and every later move of either footprint recomputes on its own.
//
// The inputs are named one per constant rather than positioned in an array, because there are seven of
// them and a read that says which name it is asking for cannot be silently rewired by a reordering.
public sealed class RainwaterHarvestReactiveHandler : MyceliumClientBase
{
    public RainwaterHarvestReactiveHandler(
        IHttpClientFactory httpClientFactory, ILogger<RainwaterHarvestReactiveHandler> logger,
        string myceliumUrl, string? serviceToken = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken)
    {
    }

    public const string BuiltFootprintInput = "builtFootprintHectares";
    public const string ProductiveFootprintInput = "productiveFootprintHectares";
    public const string RainfallInput = "rainfallMillimetresPerYear";
    public const string RunoffCoefficientInput = "runoffCoefficient";
    public const string PopulationInput = "population";
    public const string PerCapitaConsumptionInput = "perCapitaConsumptionM3";
    public const string IrrigationDemandRateInput = "irrigationDemandM3PerHectarePerYear";

    public const string HarvestOutput = "harvestM3PerYear";
    public const string DomesticDemandOutput = "domesticDemandM3PerYear";
    public const string IrrigationDemandOutput = "irrigationDemandM3PerYear";
    public const string TotalDemandOutput = "totalWaterDemandM3PerYear";
    public const string PctOfWaterDemandOutput = "pctOfWaterDemand";

    // The names as a set, for the subscription that recomputes when one moves. Built from the same
    // constants the read uses, so the filter cannot come to disagree with what Compute reads — and
    // holding none of the outputs, because this writes all five onto the study it watches.
    public static readonly IReadOnlySet<string> InputProperties = new HashSet<string>(
        [
            BuiltFootprintInput, ProductiveFootprintInput, RainfallInput, RunoffCoefficientInput,
            PopulationInput, PerCapitaConsumptionInput, IrrigationDemandRateInput,
        ],
        StringComparer.Ordinal);

    public async Task<RainwaterHarvestOutputs> RecomputeAsync(
        Guid studyId, CancellationToken cancellationToken = default)
    {
        var properties = new StudyProperties(
            await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10)), MyceliumUrl, "RainwaterHarvest");

        var inputs = await properties.ReadAsync(studyId, cancellationToken);
        var result = RainwaterHarvestCalculator.Compute(new RainwaterHarvestInputs(
            inputs.Number(BuiltFootprintInput),
            inputs.Number(RainfallInput),
            inputs.Number(RunoffCoefficientInput),
            inputs.Number(PopulationInput),
            inputs.Number(PerCapitaConsumptionInput),
            inputs.Number(ProductiveFootprintInput),
            inputs.Number(IrrigationDemandRateInput)));

        await properties.WriteAsync(studyId, HarvestOutput, result.HarvestM3PerYear, cancellationToken);
        await properties.WriteAsync(studyId, DomesticDemandOutput, result.DomesticDemandM3PerYear, cancellationToken);
        await properties.WriteAsync(studyId, IrrigationDemandOutput, result.IrrigationDemandM3PerYear, cancellationToken);
        await properties.WriteAsync(studyId, TotalDemandOutput, result.TotalDemandM3PerYear, cancellationToken);
        await properties.WriteAsync(studyId, PctOfWaterDemandOutput, result.PctOfWaterDemand, cancellationToken);
        return result;
    }
}
