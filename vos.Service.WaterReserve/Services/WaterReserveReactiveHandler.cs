using vos.Service.Shared;

namespace vos.Service.WaterReserve.Services;

// The reactive (model-driven) form of the water analysis (User Story #5839). It reacts to a graph relationship whose
// subject is the SiteStudy: reads its inputs straight off the study's effective properties, computes with
// WaterReserveCalculator, and writes its outputs back onto the study as Facts — so the study's
// WaterResilient range re-evaluates. No pipeline, no wires: the compute is a value on the study.
public sealed class WaterReserveReactiveHandler : MyceliumClientBase
{
    public WaterReserveReactiveHandler(
        IHttpClientFactory httpClientFactory, ILogger<WaterReserveReactiveHandler> logger,
        string myceliumUrl, string? serviceToken = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken)
    {
    }

    // Input port names, read off the study by name.
    private static readonly string[] Inputs = { "population", "perCapitaConsumptionM3", "storageCapacityM3" };

    // The same names as a set, for the subscription that recomputes when one moves. Derived from Inputs
    // rather than restated, so the filter cannot come to disagree with what Compute reads.
    public static readonly IReadOnlySet<string> InputProperties = new HashSet<string>(Inputs, StringComparer.Ordinal);

    // Read the study's inputs, compute, and write the outputs back onto it. Returns the outputs.
    public async Task<WaterReserveOutputs> RecomputeAsync(Guid studyId, CancellationToken cancellationToken = default)
    {
        var properties = new StudyProperties(
            await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10)), MyceliumUrl, "WaterReserve");

        var inputs = await properties.ReadAsync(studyId, cancellationToken);
        var result = WaterReserveCalculator.Compute(new WaterReserveInputs(
            inputs.Number(Inputs[0]), inputs.Number(Inputs[1]), inputs.Number(Inputs[2])));

        await properties.WriteAsync(studyId, "daysOfSupply", result.DaysOfSupply, cancellationToken);
        await properties.WriteAsync(studyId, "emergencyReserveM3", result.EmergencyReserveM3, cancellationToken);
        await properties.WriteAsync(studyId, "annualConsumptionM3", result.AnnualConsumptionM3, cancellationToken);
        await properties.WriteAsync(studyId, "pctAnnualConsumption", result.PctAnnualConsumption, cancellationToken);
        return result;
    }
}
