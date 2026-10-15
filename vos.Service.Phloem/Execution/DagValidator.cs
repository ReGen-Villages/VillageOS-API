using System.Text.Json;
using vos.Service.Phloem.Model;
using vos.Service.Shared;

namespace vos.Service.Phloem.Execution;

public sealed record DagValidationResult(bool IsValid, IReadOnlyList<Guid> Order, IReadOnlyList<string> Errors)
{
    public static DagValidationResult Invalid(IReadOnlyList<string> errors) =>
        new(false, Array.Empty<Guid>(), errors);
}

// Validates a PipelineDag up front (distinct from Hyphae's runtime oscillation
// detection): every wire connects a real out-port to a real in-port of compatible type, and the graph is
// acyclic — proven by a Kahn topological sort that orders every node.
public static class DagValidator
{
    public static DagValidationResult Validate(PipelineDag dag)
    {
        var errors = new List<string>(dag.Problems);

        foreach (var wire in dag.Wires)
            ValidateWire(dag, wire, errors);

        foreach (var node in dag.Nodes)
        {
            foreach (var port in RequiredInputs(node).Where(port => !Filled(dag, node, port.PortName)))
                errors.Add($"'{node.Name}' needs its port '{port.PortName}' filled, but no wire, parameter or choice fills it.");
            var collections = node.Ports.Where(port => port.IsInput && port.Collection).Select(port => port.PortName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (collections.Count > 1)
                errors.Add($"'{node.Name}' has {collections.Count} collection inputs ({string.Join(", ", collections.Select(name => $"'{name}'"))}); a node runs once for each item of one.");
        }

        foreach (var node in dag.Nodes.Where(node => node.Waits is not null))
            ValidateWait(dag, node, node.Waits!, errors);

        foreach (var node in dag.Nodes.Where(node => node.HandsOverTo is not null && node.CollectionInput is not null))
            errors.Add($"'{node.Name}' hands its subject over, but runs once for each item of '{node.CollectionInput!.PortName}'; a node that hands over runs once.");

        var order = TopoSort(dag, out var cyclicNodeIds);
        if (cyclicNodeIds.Count > 0)
        {
            var names = cyclicNodeIds.Select(id => dag.Node(id)?.Name ?? id.ToString());
            errors.Add($"Pipeline has a cycle; unschedulable nodes: {string.Join(", ", names)}.");
        }

        return errors.Count == 0
            ? new DagValidationResult(true, order, errors)
            : DagValidationResult.Invalid(errors);
    }

    private static void ValidateWire(PipelineDag dag, DagWire wire, List<string> errors)
    {
        var from = dag.Node(wire.FromNodeId);
        var to = dag.Node(wire.ToNodeId);
        if (from == null || to == null) return; // builder already drops wires leaving the pipeline

        var outPort = from.Port(wire.FromPort);
        var inPort = to.Port(wire.ToPort);

        if (outPort is null || !outPort.IsOutput)
        {
            errors.Add($"Wire {from.Name}.{wire.FromPort} → {to.Name}.{wire.ToPort}: '{wire.FromPort}' is not an output port on '{from.Name}'.");
            return;
        }
        if (inPort is null || !inPort.IsInput)
        {
            errors.Add($"Wire {from.Name}.{wire.FromPort} → {to.Name}.{wire.ToPort}: '{wire.ToPort}' is not an input port on '{to.Name}'.");
            return;
        }
        if (!TypesCompatible(outPort.Type, inPort.Type))
            errors.Add($"Wire {from.Name}.{wire.FromPort} → {to.Name}.{wire.ToPort}: type '{outPort.Type}' is not compatible with '{inPort.Type}'.");

        // A wire's JSONata transform must compile — caught here before dispatch, not as a silent runtime failure.
        if (!string.IsNullOrEmpty(wire.Transform) && JsonataTransform.Validate(wire.Transform) is { } transformError)
            errors.Add($"Wire {from.Name}.{wire.FromPort} → {to.Name}.{wire.ToPort}: invalid transform — {transformError}");
    }

    // A vigil is placed over one Thing, so a node that would run once per item, or wait for a Thing nothing
    // hands it, could never be resumed.
    private static void ValidateWait(PipelineDag dag, DagNode node, DagWait wait, List<string> errors)
    {
        if (node.CollectionInput is { } collection)
            errors.Add($"'{node.Name}' waits, but runs once for each item of '{collection.PortName}'; a node that waits runs once.");

        if (!Filled(dag, node, wait.PortName))
            errors.Add($"'{node.Name}' waits for the Thing reaching its port '{wait.PortName}', but nothing fills that port.");
    }

    // Asked only when a run starts: a resumed run's parameters were judged when it started.
    public static IReadOnlyList<string> UnfilledByParameters(PipelineDag dag, JsonElement parameters)
    {
        var errors = new List<string>();
        foreach (var node in dag.Nodes)
            foreach (var port in RequiredInputs(node))
            {
                if (WiredInto(dag, node, port.PortName) || node.Choices.ContainsKey(port.PortName)
                    || !node.ParameterBindings.TryGetValue(port.PortName, out var key))
                    continue;
                if (parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty(key, out _))
                    errors.Add($"'{node.Name}' needs its port '{port.PortName}' filled from the run parameter '{key}', which the run was not given.");
            }
        return errors;
    }

    // A node's own port is listed before its service's of the same name and wins, so each name is judged once.
    private static IEnumerable<DagPort> RequiredInputs(DagNode node) =>
        node.Ports.Where(port => port.IsInput)
            .DistinctBy(port => port.PortName, StringComparer.OrdinalIgnoreCase)
            .Where(port => port.Required);

    private static bool Filled(PipelineDag dag, DagNode node, string portName) =>
        WiredInto(dag, node, portName) || node.ParameterBindings.ContainsKey(portName) || node.Choices.ContainsKey(portName);

    private static bool WiredInto(PipelineDag dag, DagNode node, string portName) =>
        dag.WiresInto(node.NodeId).Any(wire => string.Equals(wire.ToPort, portName, StringComparison.OrdinalIgnoreCase));

    // Equal types are compatible; an empty/"any" type on either side is a wildcard.
    private static bool TypesCompatible(string outType, string inType) =>
        string.IsNullOrWhiteSpace(outType) || string.IsNullOrWhiteSpace(inType)
        || string.Equals(outType, "any", StringComparison.OrdinalIgnoreCase)
        || string.Equals(inType, "any", StringComparison.OrdinalIgnoreCase)
        || string.Equals(outType, inType, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<Guid> TopoSort(PipelineDag dag, out IReadOnlyList<Guid> cyclic)
    {
        var indeg = dag.Nodes.ToDictionary(n => n.NodeId, _ => 0);
        var outEdges = dag.Nodes.ToDictionary(n => n.NodeId, _ => new List<Guid>());
        foreach (var wire in dag.Wires)
            if (indeg.ContainsKey(wire.FromNodeId) && indeg.ContainsKey(wire.ToNodeId))
            {
                outEdges[wire.FromNodeId].Add(wire.ToNodeId);
                indeg[wire.ToNodeId]++;
            }

        var queue = new Queue<Guid>(dag.Nodes.Where(n => indeg[n.NodeId] == 0).Select(n => n.NodeId));
        var order = new List<Guid>();
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            order.Add(id);
            foreach (var next in outEdges[id])
                if (--indeg[next] == 0) queue.Enqueue(next);
        }

        cyclic = order.Count == dag.Nodes.Count
            ? Array.Empty<Guid>()
            : dag.Nodes.Select(n => n.NodeId).Except(order).ToList();
        return order;
    }
}
