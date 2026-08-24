using vos.Service.Shared;

namespace vos.Service.EnergyBalance.Services;

// The reactive (model-driven) form of the energy analysis (User Story #5839). Instead of running as a DAG node with
// wired ports, it reacts to a graph relationship whose subject is the SiteStudy: it reads its inputs straight off
// the study's effective properties and computes with EnergyBalanceCalculator.
//
// The generation figures and the coverage percentage are no longer written back. The shared analysis declares
// them as expressions over the study's own values, so the model derives them and refuses a written one — and
// because the refusal throws, a service that still wrote the first would abandon every write after it. What is
// left here is the one output an expression cannot hold: an expression yields a number, and this is a verdict.
// The ranges never read it — all three judge pctOfConsumption — so nothing about a balance now waits on this
// service. The node form beside it still answers for the same figures over wired ports.
public sealed class EnergyBalanceReactiveHandler : MyceliumClientBase
{
    public EnergyBalanceReactiveHandler(
        IHttpClientFactory httpClientFactory, ILogger<EnergyBalanceReactiveHandler> logger,
        string myceliumUrl, string? serviceToken = null, string? apiKey = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken, apiKey: apiKey)
    {
    }

    // Input port names, read off the study by name. solarPvAreaM2 and otherGenerationMwhPerYear are
    // roll-ups; Mycelium publishes a derived value on the Thing that owns it, so a member change arrives
    // as a change on the study like any other.
    private static readonly string[] Inputs =
    {
        "solarPvAreaM2", "solarResourceKwhPerM2PerYear", "moduleEfficiency",
        "performanceRatio", "otherGenerationMwhPerYear", "annualConsumptionMwhPerYear",
    };

    // The same names as a set, for the subscription that recomputes when one moves. Derived from Inputs
    // rather than restated, so the filter cannot come to disagree with what Compute reads.
    public static readonly IReadOnlySet<string> InputProperties = new HashSet<string>(Inputs, StringComparer.Ordinal);

    public async Task<EnergyBalanceOutputs> RecomputeAsync(Guid studyId, CancellationToken cancellationToken = default)
    {
        var properties = new StudyProperties(
            await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10)), MyceliumUrl, "EnergyBalance");

        var inputs = await properties.ReadAsync(studyId, cancellationToken);
        var result = EnergyBalanceCalculator.Compute(new EnergyBalanceInputs(
            inputs.Number(Inputs[0]), inputs.Number(Inputs[1]), inputs.Number(Inputs[2]),
            inputs.Number(Inputs[3]), inputs.Number(Inputs[4]), inputs.Number(Inputs[5])));

        await properties.WriteAsync(studyId, "netPositive", result.NetPositive, cancellationToken);
        return result;
    }
}
