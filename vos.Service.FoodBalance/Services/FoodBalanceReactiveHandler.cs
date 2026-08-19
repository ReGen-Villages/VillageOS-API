using vos.Service.Shared;

namespace vos.Service.FoodBalance.Services;

// The reactive form, and the only one (#6020): a relationship naming the study makes this read the
// study's effective properties, compute, and write the two outputs back onto it as Facts, so the ranges
// that judge them re-evaluate.
//
// Its productive area is written by LandAllocation onto the same study, which puts this on the second
// layer of the analysis: one dispatch, and every later move of that footprint recomputes on its own.
public sealed class FoodBalanceReactiveHandler : MyceliumClientBase
{
    public FoodBalanceReactiveHandler(
        IHttpClientFactory httpClientFactory, ILogger<FoodBalanceReactiveHandler> logger,
        string myceliumUrl, string? serviceToken = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken)
    {
    }

    public const string PeopleFedOutput = "peopleFed";
    public const string PctOfPopulationFedOutput = "pctOfPopulationFed";

    // Read off the study by name. The footprint is land allocation's output, the yield is an assumption
    // inherited from the shared SiteStudy archetype, and the population is the site's own figure.
    private static readonly string[] Inputs =
        ["productiveFootprintHectares", "peopleFedPerHectarePerYear", "population"];

    // The same names as a set, for the subscription that recomputes when one moves. Derived from Inputs
    // rather than restated, so the filter cannot come to disagree with what Compute reads — and holding
    // neither output, because this writes both onto the study it watches.
    public static readonly IReadOnlySet<string> InputProperties = new HashSet<string>(Inputs, StringComparer.Ordinal);

    public async Task<FoodBalanceOutputs> RecomputeAsync(Guid studyId, CancellationToken cancellationToken = default)
    {
        var study = new StudyProperties(
            await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10)), MyceliumUrl, "FoodBalance");

        var inputs = await study.ReadAsync(studyId, cancellationToken);
        var result = FoodBalanceCalculator.Compute(new FoodBalanceInputs(
            inputs.Number(Inputs[0]), inputs.Number(Inputs[1]), inputs.Number(Inputs[2])));

        await study.WriteAsync(studyId, PeopleFedOutput, result.PeopleFed, cancellationToken);
        await study.WriteAsync(studyId, PctOfPopulationFedOutput, result.PctOfPopulationFed, cancellationToken);
        return result;
    }
}
