using vos.Service.Shared;

namespace vos.Service.RainwaterHarvest.Services;

// The reactive form, and the only one (#6020): a relationship naming the study makes this read the
// study's effective properties, compute, and write the harvest, its three demand figures and the
// percentage between them back onto it as Facts, so the ranges that judge them re-evaluate.
//
// Both footprints are written by LandAllocation onto the same study, which puts this on the second layer
// of the analysis: one dispatch, and every later move of either footprint recomputes on its own.
//
// Inputs are named one per constant and passed by name, rather than positioned in an array as the three
// services beside it do. There are seven, close enough in meaning that two of them are areas in hectares
// and two are demand rates, and neither the read nor the call can then be silently rewired by a
// reordering.
public sealed class RainwaterHarvestReactiveHandler : MyceliumClientBase
{
    public RainwaterHarvestReactiveHandler(
        IHttpClientFactory httpClientFactory, ILogger<RainwaterHarvestReactiveHandler> logger,
        string myceliumUrl, string? serviceToken = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken)
    {
    }

    // Private, because InputProperties below already publishes the same names as the set a caller reads.
    private const string BuiltFootprintInput = "builtFootprintHectares";
    private const string ProductiveFootprintInput = "productiveFootprintHectares";
    private const string RainfallInput = "rainfallMillimetresPerYear";
    private const string RunoffCoefficientInput = "runoffCoefficient";
    private const string PopulationInput = "population";
    private const string PerCapitaConsumptionInput = "perCapitaConsumptionM3";
    private const string IrrigationDemandRateInput = "irrigationDemandM3PerHectarePerYear";

    public const string HarvestOutput = "harvestM3PerYear";
    public const string DomesticDemandOutput = "domesticDemandM3PerYear";
    public const string IrrigationDemandOutput = "irrigationDemandM3PerYear";
    public const string TotalWaterDemandOutput = "totalWaterDemandM3PerYear";
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
            BuiltFootprintHectares: inputs.Number(BuiltFootprintInput),
            RainfallMillimetresPerYear: inputs.Number(RainfallInput),
            RunoffCoefficient: inputs.Number(RunoffCoefficientInput),
            PopulationResidents: inputs.Number(PopulationInput),
            PerCapitaConsumptionM3PerYear: inputs.Number(PerCapitaConsumptionInput),
            ProductiveFootprintHectares: inputs.Number(ProductiveFootprintInput),
            IrrigationDemandM3PerHectarePerYear: inputs.Number(IrrigationDemandRateInput)));

        await properties.WriteAsync(studyId, HarvestOutput, result.HarvestM3PerYear, cancellationToken);
        await properties.WriteAsync(studyId, DomesticDemandOutput, result.DomesticDemandM3PerYear, cancellationToken);
        await properties.WriteAsync(studyId, IrrigationDemandOutput, result.IrrigationDemandM3PerYear, cancellationToken);
        await properties.WriteAsync(studyId, TotalWaterDemandOutput, result.TotalWaterDemandM3PerYear, cancellationToken);
        await properties.WriteAsync(studyId, PctOfWaterDemandOutput, result.PctOfWaterDemand, cancellationToken);
        return result;
    }
}
