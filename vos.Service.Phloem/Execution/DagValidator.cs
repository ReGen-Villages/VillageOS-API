using System.Text.Json;
using vos.Service.Phloem.Model;
using vos.Service.Shared;

namespace vos.Service.Phloem.Execution;

// Every reason a drawing cannot run, judged before anything runs; Hyphae's oscillation detection judges a run while it runs.
public static class DagValidator
{
    public static IReadOnlyList<string> Validate(PipelineDag dag)
    {
        var errors = new List<string>(dag.Problems);

        foreach (var wire in dag.Wires)
            ValidateWire(wire, errors);

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

        foreach (var node in dag.Nodes)
            ValidateDecisionPaths(dag, node, errors);

        var outputNodes = dag.Nodes.Where(node => node.Kind == DagNodeKind.Output).ToList();
        if (outputNodes.Count != 1)
            errors.Add($"'{dag.Name}' has {outputNodes.Count} Output nodes; every path ends at one, which reports how the run ended.");
        else
            foreach (var node in NodesThatCannotReach(dag, outputNodes[0]))
                errors.Add($"'{node.Name}' cannot reach '{outputNodes[0].Name}', so a run taking its path would end without reporting how.");

        var unschedulable = UnschedulableNodes(dag);
        if (unschedulable.Count > 0)
        {
            var names = unschedulable.Select(id => dag.Node(id)?.Name ?? id.ToString());
            errors.Add($"Pipeline has a cycle; unschedulable nodes: {string.Join(", ", names)}.");
        }

        return errors;
    }

    private static void ValidateDecisionPaths(PipelineDag dag, DagNode node, List<string> errors)
    {
        foreach (var path in node.DecisionPaths.Where(path => !dag.WiresOutOf(node.NodeId).Any(wire => wire.TakenOn.Contains(path.Id))))
            errors.Add($"'{node.Name}' has the choice '{path.Name}', but no wire leaving it is taken on that choice.");

        var withoutARule = node.DecisionPaths.Where(path => path.When is null).Select(path => $"'{path.Name}'").ToList();
        if (withoutARule.Count > 1)
            errors.Add($"'{node.Name}' has {withoutARule.Count} choices without a rule ({string.Join(", ", withoutARule)}); only one can be taken when no rule holds.");
    }

    private static void ValidateWire(DagWire wire, List<string> errors)
    {
        var (from, to) = (wire.From, wire.To);
        var named = $"Wire {from.Node.Name}.{from.Port.PortName} → {to.Node.Name}.{to.Port.PortName}";

        if (!TypesCompatible(from.Port.Type, to.Port.Type))
            errors.Add($"{named}: type '{from.Port.Type}' is not compatible with '{to.Port.Type}'.");

        // A wire's JSONata transform must compile — caught here before any call, not as a silent runtime failure.
        if (!string.IsNullOrEmpty(wire.Transform) && JsonataTransform.Validate(wire.Transform) is { } transformError)
            errors.Add($"{named}: invalid transform — {transformError}");
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
        dag.WiresInto(node.NodeId).Any(wire => wire.To.Port.IsNamed(portName));

    // Equal types are compatible; an empty/"any" type on either side is a wildcard.
    private static bool TypesCompatible(string outType, string inType) =>
        string.IsNullOrWhiteSpace(outType) || string.IsNullOrWhiteSpace(inType)
        || string.Equals(outType, "any", StringComparison.OrdinalIgnoreCase)
        || string.Equals(inType, "any", StringComparison.OrdinalIgnoreCase)
        || string.Equals(outType, inType, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<DagNode> NodesThatCannotReach(PipelineDag dag, DagNode end)
    {
        var reaching = new HashSet<Guid> { end.NodeId };
        var frontier = new Stack<Guid>([end.NodeId]);
        while (frontier.Count > 0)
            foreach (var wire in dag.WiresInto(frontier.Pop()))
                if (reaching.Add(wire.From.Node.NodeId))
                    frontier.Push(wire.From.Node.NodeId);
        return dag.Nodes.Where(node => !reaching.Contains(node.NodeId));
    }

    // A Kahn topological sort, which never reaches a node on a cycle or after one.
    private static IReadOnlyList<Guid> UnschedulableNodes(PipelineDag dag)
    {
        var indeg = dag.Nodes.ToDictionary(n => n.NodeId, _ => 0);
        var outEdges = dag.Nodes.ToDictionary(n => n.NodeId, _ => new List<Guid>());
        foreach (var wire in dag.Wires)
        {
            outEdges[wire.From.Node.NodeId].Add(wire.To.Node.NodeId);
            indeg[wire.To.Node.NodeId]++;
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

        return dag.Nodes.Select(n => n.NodeId).Except(order).ToList();
    }
}
