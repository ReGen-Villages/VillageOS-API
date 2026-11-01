using System.Text.Json;
using System.Text.Json.Nodes;
using vos.Service.Phloem.Model;

namespace vos.Service.Phloem.Execution;

// CarriesAFail is true when a fail answer chose the wire, and the Output node counts it among the run's failures.
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

    // A node that ran once sends each element of a list it answered, or the value itself, numbered afresh.
    public static IEnumerable<NumberedItem> ItemsAlong(PipelineDag dag, DagWire wire, NodeRunResult result)
    {
        if (result.Items is { } items)
        {
            var failedAsAWhole = result.Status == RunStatus.Failed;
            return items.Where(item => !failedAsAWhole || item.Status == RunStatus.Failed)
                .Select(item => ItemAlong(dag, wire, item))
                .OfType<NumberedItem>()
                .ToList();
        }
        if (Delivered(dag, wire, result) is not { } delivery || PayloadMapping.Extract(delivery.Value, wire.FromPath) is not { } value)
            return [];
        var values = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToList() : [value];
        return values.Select((element, index) => new NumberedItem(element.Clone(), new ItemNumber(index, values.Count))).ToList();
    }

    public static NumberedItem? ItemAlong(PipelineDag dag, DagWire wire, NodeRunResult item) =>
        item.Number is { } number && Chooses(dag, wire, item) && PayloadMapping.Extract(ValueAt(dag, wire.From.Port, item), wire.FromPath) is { } value
            ? new NumberedItem(value, number)
            : null;

    private static bool Chooses(PipelineDag dag, DagWire wire, NodeRunResult answer) => answer.Status switch
    {
        RunStatus.Failed => wire.TakenOn.Contains(dag.Fail),
        RunStatus.Succeeded => wire.TakenOn.Contains(answer.Decision ?? dag.Success),
        RunStatus.Rehearsed => wire.TakenOn.Contains(dag.Success),
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
