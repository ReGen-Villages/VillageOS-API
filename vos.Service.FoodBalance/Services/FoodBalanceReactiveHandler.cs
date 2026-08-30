using vos.Service.Shared;

namespace vos.Service.FoodBalance.Services;

// The reactive form, and the only one (#6020): a relationship naming the study makes this read the
// study's effective properties and compute.
//
// Neither figure is written back any more. The shared analysis declares both as expressions over the
// study's own values, so the model derives them and refuses a written one — and because the refusal
// throws, a service that still wrote the first would abandon every write after it. Nothing is left for
// this to assert, so the answer now only reaches a caller of /handle.
//
// Its productive area is written by LandAllocation onto the same study, which puts this on the second
// layer of the analysis: one dispatch, and every later move of that footprint recomputes on its own.
public sealed class FoodBalanceReactiveHandler : MyceliumClientBase
{
    public FoodBalanceReactiveHandler(
        IHttpClientFactory httpClientFactory, ILogger<FoodBalanceReactiveHandler> logger,
        string myceliumUrl, string? serviceToken = null, string? apiKey = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken, apiKey: apiKey)
    {
    }

    // Read off the study by name. The footprint is land allocation's output, the yield is an assumption
    // inherited from the shared SiteStudy archetype, and the population is the site's own figure.
    private static readonly string[] Inputs =
        ["productiveFootprintHectares", "peopleFedPerHectarePerYear", "population"];

    // The same names as a set, for the subscription that recomputes when one moves. Derived from Inputs
    // rather than restated, so the filter cannot come to disagree with what Compute reads.
    public static readonly IReadOnlySet<string> InputProperties = new HashSet<string>(Inputs, StringComparer.Ordinal);

    public async Task<RecomputeAnswer<FoodBalanceOutputs>> RecomputeAsync(
        Guid studyId, CancellationToken cancellationToken = default)
    {
        var properties = new StudyProperties(
            await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10)), MyceliumUrl, "FoodBalance");

        var inputs = await properties.ReadAsync(studyId, cancellationToken);
        if (inputs.WaitingFor(Inputs) is { Count: > 0 } waitingFor)
        {
            Logger.LogInformation(
                "FoodBalance: the study {StudyId} has no number for {Inputs} yet, so no balance is worked out",
                studyId, string.Join(", ", waitingFor));
            return new(null, waitingFor);
        }

        return new(FoodBalanceCalculator.Compute(new FoodBalanceInputs(
            inputs.Number(Inputs[0]), inputs.Number(Inputs[1]), inputs.Number(Inputs[2]))), []);
    }
}
