using vos.Service.Phloem.Model;
using vos.Service.Shared;

namespace vos.Service.Phloem.Execution;

// The result of validating a DAG: a topological order when valid, or the reasons it isn't.
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
        var errors = new List<string>();

        foreach (var wire in dag.Wires)
            ValidateWire(dag, wire, errors);

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

        // A wire's JSONata transform must compile — caught here before dispatch, not as a silent runtime failure (#5875).
        if (!string.IsNullOrEmpty(wire.Transform) && JsonataTransform.Validate(wire.Transform) is { } transformError)
            errors.Add($"Wire {from.Name}.{wire.FromPort} → {to.Name}.{wire.ToPort}: invalid transform — {transformError}");
    }

    // Equal types are compatible; an empty/"any" type on either side is a wildcard.
    private static bool TypesCompatible(string outType, string inType) =>
        string.IsNullOrWhiteSpace(outType) || string.IsNullOrWhiteSpace(inType)
        || string.Equals(outType, "any", StringComparison.OrdinalIgnoreCase)
        || string.Equals(inType, "any", StringComparison.OrdinalIgnoreCase)
        || string.Equals(outType, inType, StringComparison.OrdinalIgnoreCase);

    // Kahn topological sort. Returns the ordered node ids; any nodes left unscheduled (in a cycle)
    // are returned via cyclic.
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
