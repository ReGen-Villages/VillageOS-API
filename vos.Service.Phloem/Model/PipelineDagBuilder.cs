using System.Text.Json;

namespace vos.Service.Phloem.Model;

// Thrown when a pipeline cannot be resolved into an executable DAG (missing binding, no subdomain,
// unknown pipeline). Distinct from validation failures (cycles / port-type mismatches).
public sealed class PipelineModelException : Exception
{
    public PipelineModelException(string message) : base(message) { }
}

// Resolves a loaded PipelineGraph + pipelineId into a PipelineDag:
// nodes, their dispatch subdomain (node has Connection, Subdomain property), ports (walk the
// bound service's is-chain), and wires (the predicate is of the wire archetype, carrying fromPort/toPort).
// Every role is read from the flag its archetype carries, never from a name.
public static class PipelineDagBuilder
{
    public static PipelineDag Build(PipelineGraph graph, Guid pipelineId)
    {
        PipelineArchetypes.RequireRolesAreMarked(graph);

        var pipeline = graph.Thing(pipelineId)
            ?? throw new PipelineModelException($"Pipeline {pipelineId} not found in the loaded subgraph.");
        if (!graph.IsOfArchetypeCarrying(pipeline, PipelineArchetypes.PipelineFlag))
            throw new PipelineModelException($"Thing {pipelineId} ('{pipeline.Name}') is not a pipeline.");

        var nodeThings = graph.OutgoingTargets(pipeline, ModelNames.Has)
            .Where(t => graph.IsOfArchetypeCarrying(t, PipelineArchetypes.PipelineNodeFlag))
            .ToList();
        if (nodeThings.Count == 0)
            throw new PipelineModelException($"Pipeline '{pipeline.Name}' has no nodes.");

        var nodeIds = nodeThings.Select(n => n.Id).ToHashSet();
        var nodes = nodeThings.Select(n => BuildNode(graph, n)).ToList();

        var wires = new List<DagWire>();
        foreach (var nodeThing in nodeThings)
            foreach (var rel in graph.OutgoingByPredicateCarrying(nodeThing, PipelineArchetypes.PipelineWireFlag))
            {
                if (!nodeIds.Contains(rel.TargetId)) continue; // ignore wires leaving the pipeline
                wires.Add(new DagWire(
                    nodeThing.Id,
                    rel.PropertyString(ModelNames.FromPort) ?? string.Empty,
                    rel.TargetId,
                    rel.PropertyString(ModelNames.ToPort) ?? string.Empty,
                    rel.PropertyString(ModelNames.FromPath) ?? string.Empty,
                    rel.PropertyString(ModelNames.ToPath) ?? string.Empty,
                    rel.PropertyString(ModelNames.Transform) ?? string.Empty));
            }

        return new PipelineDag
        {
            PipelineId = pipelineId,
            Name = pipeline.Name,
            Nodes = nodes,
            Wires = wires,
        };
    }

    private static DagNode BuildNode(PipelineGraph graph, GraphThing nodeThing)
    {
        // Boundary nodes (#5873) bind no Connection/Service: they declare their own ports (has → Port) and
        // are an Input source (params → outputs) or an Output sink (inputs → run result).
        if (graph.IsOfArchetypeCarrying(nodeThing, PipelineArchetypes.PipelineInputFlag))
            return BuildBoundaryNode(graph, nodeThing, DagNodeKind.Input);
        if (graph.IsOfArchetypeCarrying(nodeThing, PipelineArchetypes.PipelineOutputFlag))
            return BuildBoundaryNode(graph, nodeThing, DagNodeKind.Output);

        var connection = graph.OutgoingTargets(nodeThing, ModelNames.Has)
            .FirstOrDefault(t => graph.IsOfArchetypeCarrying(t, PipelineArchetypes.ConnectionFlag))
            ?? throw new PipelineModelException($"Node '{nodeThing.Name}' binds no service connection.");

        var subdomain = connection.PropertyString(ModelNames.Subdomain);
        if (string.IsNullOrWhiteSpace(subdomain))
            throw new PipelineModelException($"Connection '{connection.Name}' for node '{nodeThing.Name}' has no {ModelNames.Subdomain}.");

        var service = graph.OutgoingTargets(connection, ModelNames.Has)
            .FirstOrDefault(t => graph.IsOfArchetypeCarrying(t, PipelineArchetypes.ServiceFlag))
            ?? throw new PipelineModelException($"Connection '{connection.Name}' binds no service.");

        return new DagNode
        {
            NodeId = nodeThing.Id,
            Name = nodeThing.Name,
            Subdomain = subdomain!,
            Params = new Dictionary<string, JsonElement>(nodeThing.Properties, StringComparer.Ordinal),
            Ports = ResolvePorts(graph, service).ToList(),
            ParamBindings = ParseParamBindings(nodeThing),
            OnItemError = string.Equals(nodeThing.PropertyString(ModelNames.OnItemError), ModelNames.OnItemErrorContinue, StringComparison.OrdinalIgnoreCase)
                ? ModelNames.OnItemErrorContinue
                : ModelNames.OnItemErrorFail,
        };
    }

    // A boundary node (#5873): ports are declared on the node itself (its own has → Port chain),
    // there is no dispatch subdomain, and its DagNodeKind tells the executor to seed from params
    // (Input) or collect into the run result (Output).
    private static DagNode BuildBoundaryNode(PipelineGraph graph, GraphThing nodeThing, DagNodeKind kind)
    {
        return new DagNode
        {
            NodeId = nodeThing.Id,
            Name = nodeThing.Name,
            Kind = kind,
            Subdomain = string.Empty,
            Params = new Dictionary<string, JsonElement>(nodeThing.Properties, StringComparer.Ordinal),
            Ports = ResolvePorts(graph, nodeThing).ToList(),
            ParamBindings = ParseParamBindings(nodeThing),
        };
    }

    // Parse the node's paramBindings property — a JSON object mapping input-port name → the
    // run-param key that fills it (#5647). Malformed/absent → no bindings (never fails a build).
    private static IReadOnlyDictionary<string, string> ParseParamBindings(GraphThing nodeThing)
    {
        var raw = nodeThing.PropertyString(ModelNames.ParamBindings);
        if (string.IsNullOrWhiteSpace(raw)) return EmptyBindings;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return EmptyBindings;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(p.Value.GetString()))
                    map[p.Name] = p.Value.GetString()!;
            return map;
        }
        catch (JsonException)
        {
            return EmptyBindings;
        }
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyBindings =
        new Dictionary<string, string>();

    // Collect Port child-Things by walking the service's is-chain — relationships do not
    // inherit through is, so ports resolve at read time at each level of the chain.
    private static IEnumerable<DagPort> ResolvePorts(PipelineGraph graph, GraphThing service)
    {
        var seen = new HashSet<Guid>();
        var stack = new Stack<GraphThing>();
        stack.Push(service);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current.Id)) continue;
            foreach (var portThing in graph.OutgoingTargets(current, ModelNames.Has)
                         .Where(t => graph.IsOfArchetypeCarrying(t, PipelineArchetypes.PortFlag)))
                yield return new DagPort(
                    portThing.PropertyString(ModelNames.PortName) ?? portThing.Name,
                    portThing.PropertyString(ModelNames.Direction) ?? ModelNames.DirectionIn,
                    portThing.PropertyString(ModelNames.PortType) ?? string.Empty,
                    string.Equals(portThing.PropertyString(ModelNames.Required), "true", StringComparison.OrdinalIgnoreCase),
                    string.Equals(portThing.PropertyString(ModelNames.Collection), "true", StringComparison.OrdinalIgnoreCase));
            foreach (var parent in graph.OutgoingTargets(current, ModelNames.Is))
                stack.Push(parent);
        }
    }
}
