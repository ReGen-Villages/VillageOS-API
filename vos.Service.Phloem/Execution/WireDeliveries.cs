using System.Text.Json;
using System.Text.Json.Nodes;
using vos.Service.Phloem.Model;

namespace vos.Service.Phloem.Execution;

// CarriesAFail is true when a fail answer chose the wire, which is what makes the Output node report fail.
public sealed record WireDelivery(JsonElement Value, bool CarriesAFail);

// Which wires leaving a settled node carry, and what each carries. A wire carries when the answer names what it
// is taken on; the port's value is delivered as answered, and a port the answer left out delivers null.
public static class WireDeliveries
{
    private static readonly JsonElement Null = JsonSerializer.SerializeToElement<object?>(null);

    public static IEnumerable<(DagWire Wire, WireDelivery Delivery)> From(PipelineDag dag, DagNode node, NodeRunResult result)
    {
        foreach (var wire in dag.WiresOutOf(node.NodeId))
            if (Delivered(dag, wire, result) is { } delivery)
                yield return (wire, delivery);
    }

    // A node that runs once per item and failed as a whole keeps only its failed items, so only its fail wires carry.
    private static WireDelivery? Delivered(PipelineDag dag, DagWire wire, NodeRunResult result)
    {
        if (result.Items is not { } items)
            return Chooses(dag, wire, result) ? new WireDelivery(ValueAt(dag, wire.From.Port, result), result.Status == RunStatus.Failed) : null;

        var failedAsAWhole = result.Status == RunStatus.Failed;
        var chosen = items.Where(item => (!failedAsAWhole || item.Status == RunStatus.Failed) && Chooses(dag, wire, item)).ToList();
        if (chosen.Count == 0) return null;
        return new WireDelivery(Gathered(dag, wire.From.Port, chosen), chosen.Any(item => item.Status == RunStatus.Failed));
    }

    private static bool Chooses(PipelineDag dag, DagWire wire, NodeRunResult answer) => answer.Status switch
    {
        RunStatus.Failed => wire.TakenOn.Contains(dag.Fail),
        RunStatus.Succeeded => answer.Decision is { } decision
            ? wire.From.Node.DecisionPaths.TryGetValue(decision, out var path) && wire.TakenOn.Contains(path)
            : wire.TakenOn.Contains(dag.Success),
        _ => false,
    };

    private static JsonElement ValueAt(PipelineDag dag, DagPort port, NodeRunResult answer)
    {
        if (port.Id == dag.ErrorPort)
            return answer.Error is { } error ? JsonSerializer.SerializeToElement(error) : Null;
        return answer.Outputs.TryGetValue(port.PortName, out var value) ? value : Null;
    }

    // An out port marked as a collection gathers flat, so a list per item reaches the next node as one list.
    private static JsonElement Gathered(PipelineDag dag, DagPort port, IReadOnlyList<NodeRunResult> chosen)
    {
        var list = new JsonArray();
        foreach (var item in chosen)
        {
            var value = ValueAt(dag, port, item);
            if (port.Collection && value.ValueKind == JsonValueKind.Array)
                foreach (var element in value.EnumerateArray()) list.Add(JsonSerializer.SerializeToNode(element));
            else if (!port.Collection || value.ValueKind != JsonValueKind.Null)
                list.Add(JsonSerializer.SerializeToNode(value));
        }
        return JsonSerializer.SerializeToElement(list);
    }
}
