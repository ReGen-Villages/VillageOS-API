using vos.Service.Shared;

namespace vos.Service.EnergyBalance.Services;

// The reactive (model-driven) form of the energy analysis (User Story #5839). Instead of running as a DAG node with
// wired ports, it reacts to a graph relationship whose subject is the SiteStudy: it reads its inputs straight off
// the study's effective properties, computes with EnergyBalanceCalculator, and writes its outputs back
// onto the study as Facts — so the study's judge ranges (e.g. EnergyNetPositive) re-evaluate. No pipeline, no
// wires: the compute is a value on the study, like a roll-up or a range.
public sealed class EnergyBalanceReactiveHandler : MyceliumClientBase
{
    public EnergyBalanceReactiveHandler(
        IHttpClientFactory httpClientFactory, ILogger<EnergyBalanceReactiveHandler> logger,
        string myceliumUrl, string? serviceToken = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken)
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

    // Read the study's inputs, compute, and write the outputs back onto it. Returns the outputs.
    public async Task<EnergyBalanceOutputs> RecomputeAsync(Guid studyId, CancellationToken cancellationToken = default)
    {
        var study = new StudyProperties(
            await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10)), MyceliumUrl, "EnergyBalance");

        var inputs = await study.ReadAsync(studyId, cancellationToken);
        var result = EnergyBalanceCalculator.Compute(new EnergyBalanceInputs(
            inputs.Number(Inputs[0]), inputs.Number(Inputs[1]), inputs.Number(Inputs[2]),
            inputs.Number(Inputs[3]), inputs.Number(Inputs[4]), inputs.Number(Inputs[5])));

        await study.WriteAsync(studyId, "pctOfConsumption", result.PctOfConsumption, cancellationToken);
        await study.WriteAsync(studyId, "netPositive", result.NetPositive, cancellationToken);
        await study.WriteAsync(studyId, "solarGenerationMwhPerYear", result.SolarGenerationMwhPerYear, cancellationToken);
        await study.WriteAsync(studyId, "totalGenerationMwhPerYear", result.TotalGenerationMwhPerYear, cancellationToken);
        return result;
    }
}
