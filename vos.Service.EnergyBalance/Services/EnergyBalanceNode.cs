using System.Text.Json;
using vos.Service.Shared.DagNode;

namespace vos.Service.EnergyBalance.Services;

// The EnergyBalance analysis as a pipeline node (Plane B / D).
// pctOfConsumption is what the Plane-C "EnergyNetPositive" range (>= 100%) judges.
public sealed class EnergyBalanceNode : DagNodeService
{
    public EnergyBalanceNode(IHttpClientFactory httpClientFactory, ILogger<EnergyBalanceNode> logger,
        string myceliumUrl, string? serviceToken = null, string? apiKey = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken, apiKey: apiKey)
    {
    }

    public override IReadOnlyList<PortDescriptor> Ports { get; } = new[]
    {
        PortDescriptor.Input("solarPvAreaM2", "number", required: true),
        PortDescriptor.Input("solarResourceKwhPerM2PerYear", "number", required: true),
        PortDescriptor.Input("moduleEfficiency", "number", required: true),
        PortDescriptor.Input("performanceRatio", "number", required: true),
        PortDescriptor.Input("otherGenerationMwhPerYear", "number", required: true),
        PortDescriptor.Input("annualConsumptionMwhPerYear", "number", required: true),
        PortDescriptor.Output("solarGenerationMwhPerYear", "number"),
        PortDescriptor.Output("totalGenerationMwhPerYear", "number"),
        PortDescriptor.Output("pctOfConsumption", "number"),
        PortDescriptor.Output("netPositive", "boolean"),
    };

    protected override Task<NodeResult> ExecuteNodeAsync(NodeContext context, CancellationToken cancellationToken)
    {
        var result = EnergyBalanceCalculator.Compute(new EnergyBalanceInputs(
            Number(context, "solarPvAreaM2"),
            Number(context, "solarResourceKwhPerM2PerYear"),
            Number(context, "moduleEfficiency"),
            Number(context, "performanceRatio"),
            Number(context, "otherGenerationMwhPerYear"),
            Number(context, "annualConsumptionMwhPerYear")));

        return Task.FromResult(NodeResult.Ok(
            ("solarGenerationMwhPerYear", (object?)result.SolarGenerationMwhPerYear),
            ("totalGenerationMwhPerYear", result.TotalGenerationMwhPerYear),
            ("pctOfConsumption", result.PctOfConsumption),
            ("netPositive", result.NetPositive)));
    }

    // Matched on the value kind rather than read straight through: a port wired to a withheld roll-up
    // arrives present-but-null, and GetDouble() answers that with .NET's own message, which names no port.
    private static double Number(NodeContext context, string port) =>
        context.Input(port) is { ValueKind: JsonValueKind.Number } value
            ? value.GetDouble()
            : throw new InvalidOperationException($"EnergyBalance node requires a numeric '{port}' input.");
}
