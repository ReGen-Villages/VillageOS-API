using vos.Service.Shared.DagNode;

namespace vos.Service.EnergyBalance.Services;

// The EnergyBalance analysis as a pipeline node (Plane B / D): PV area + solar resource + efficiency +
// other generation + consumption in; solar/total generation, % of consumption, and net-positive out.
// pctOfConsumption is what the Plane-C "EnergyNetPositive" range (>= 100%) judges.
public sealed class EnergyBalanceNode : DagNodeService
{
    public EnergyBalanceNode(IHttpClientFactory httpClientFactory, ILogger<EnergyBalanceNode> logger,
        string myceliumUrl, string? serviceToken = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken)
    {
    }

    public override IReadOnlyList<PortDescriptor> Ports { get; } = new[]
    {
        PortDescriptor.Input("solarPvAreaM2", "number", required: true),
        PortDescriptor.Input("solarResourceKwhPerM2PerYear", "number", required: true),
        PortDescriptor.Input("pvEfficiency", "number", required: true),
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
            Number(context, "pvEfficiency"),
            Number(context, "otherGenerationMwhPerYear"),
            Number(context, "annualConsumptionMwhPerYear")));

        return Task.FromResult(NodeResult.Ok(
            ("solarGenerationMwhPerYear", (object?)result.SolarGenerationMwhPerYear),
            ("totalGenerationMwhPerYear", result.TotalGenerationMwhPerYear),
            ("pctOfConsumption", result.PctOfConsumption),
            ("netPositive", result.NetPositive)));
    }

    private static double Number(NodeContext context, string port) =>
        context.Input(port)?.GetDouble()
        ?? throw new InvalidOperationException($"EnergyBalance node requires a numeric '{port}' input.");
}
