using System.Text.Json;
using vos.Service.Shared.DagNode;

namespace vos.Service.WaterReserve.Services;

// The WaterReserve analysis as a pipeline node (Plane B / D): population + per-capita rate + stored
// volume in; emergency reserve, days-of-supply, and % annual consumption out. daysOfSupply is what the
// Plane-C "WaterResilient" range (>= 14 days) judges.
public sealed class WaterReserveNode : DagNodeService
{
    public WaterReserveNode(IHttpClientFactory httpClientFactory, ILogger<WaterReserveNode> logger,
        string myceliumUrl, string? serviceToken = null, string? apiKey = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken, apiKey: apiKey)
    {
    }

    public override IReadOnlyList<PortDescriptor> Ports { get; } = new[]
    {
        PortDescriptor.Input("population", "number", required: true),
        PortDescriptor.Input("perCapitaConsumptionM3", "number", required: true),
        PortDescriptor.Input("storageCapacityM3", "number", required: true),
        PortDescriptor.Output("emergencyReserveM3", "number"),
        PortDescriptor.Output("annualConsumptionM3", "number"),
        PortDescriptor.Output("pctAnnualConsumption", "number"),
        PortDescriptor.Output("daysOfSupply", "number"),
    };

    protected override Task<NodeResult> ExecuteNodeAsync(NodeContext context, CancellationToken cancellationToken)
    {
        var result = WaterReserveCalculator.Compute(new WaterReserveInputs(
            Number(context, "population"),
            Number(context, "perCapitaConsumptionM3"),
            Number(context, "storageCapacityM3")));

        return Task.FromResult(NodeResult.Ok(
            ("emergencyReserveM3", (object?)result.EmergencyReserveM3),
            ("annualConsumptionM3", result.AnnualConsumptionM3),
            ("pctAnnualConsumption", result.PctAnnualConsumption),
            ("daysOfSupply", result.DaysOfSupply)));
    }

    // Matched on the value kind rather than read straight through: a port wired to a withheld roll-up
    // arrives present-but-null, and GetDouble() answers that with .NET's own message, which names no port.
    private static double Number(NodeContext context, string port) =>
        context.Input(port) is { ValueKind: JsonValueKind.Number } value
            ? value.GetDouble()
            : throw new InvalidOperationException($"WaterReserve node requires a numeric '{port}' input.");
}
