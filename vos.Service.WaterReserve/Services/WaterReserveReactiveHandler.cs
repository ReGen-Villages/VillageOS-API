using vos.Service.Shared;

namespace vos.Service.WaterReserve.Services;

// The reactive (model-driven) form of the water analysis (User Story #5839). It reacts to a graph relationship whose
// subject is the SiteStudy: reads its inputs straight off the study's effective properties and computes with
// WaterReserveCalculator.
//
// None of the four figures is written back any more. The shared analysis declares all of them as expressions
// over the study's own values, so the model derives them and refuses a written one — and because the refusal
// throws, a service that still wrote the first would abandon every write after it. Nothing is left for this
// form to assert, so the answer now only reaches a caller of /handle.
public sealed class WaterReserveReactiveHandler : MyceliumClientBase
{
    public WaterReserveReactiveHandler(
        IHttpClientFactory httpClientFactory, ILogger<WaterReserveReactiveHandler> logger,
        string myceliumUrl, string? serviceToken = null, string? apiKey = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken, apiKey: apiKey)
    {
    }

    // Input port names, read off the study by name.
    private static readonly string[] Inputs = { "population", "perCapitaConsumptionM3", "storageCapacityM3" };

    // The same names as a set, for the subscription that recomputes when one moves. Derived from Inputs
    // rather than restated, so the filter cannot come to disagree with what Compute reads.
    public static readonly IReadOnlySet<string> InputProperties = new HashSet<string>(Inputs, StringComparer.Ordinal);

    public async Task<RecomputeAnswer<WaterReserveOutputs>> RecomputeAsync(
        Guid studyId, CancellationToken cancellationToken = default)
    {
        var properties = new StudyProperties(
            await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10)), MyceliumUrl, "WaterReserve");

        var inputs = await properties.ReadAsync(studyId, cancellationToken);
        if (inputs.WaitingFor(Inputs) is { Count: > 0 } waitingFor)
        {
            Logger.LogInformation(
                "WaterReserve: the study {StudyId} carries no {Inputs}, so no reserve is worked out",
                studyId, string.Join(", ", waitingFor));
            return new(null, waitingFor);
        }

        return new(WaterReserveCalculator.Compute(new WaterReserveInputs(
            inputs.Number(Inputs[0]), inputs.Number(Inputs[1]), inputs.Number(Inputs[2]))), []);
    }
}
