using System.Globalization;
using System.Text.Json;
using vos.Service.Shared;
using vos.Service.Shared.Roles;

namespace vos.Service.Phloem.Model;

// Thrown when nothing in a drawing can be judged: the roles are unbound, or the Thing is not a pipeline with
// nodes. A problem with one node is listed on the DAG's Problems instead.
public sealed class PipelineModelException : Exception
{
    public PipelineModelException(string message) : base(message) { }
}

// Resolves a loaded PipelineGraph + pipelineId into a PipelineDag:
// nodes, the subdomain they are called at (node has Connection, Subdomain property), ports (walk the
// bound service's is-chain), and wires (Things the node has, related to the two ports they connect).
// Every role is read from the role Thing that binds it, never from a name.
public static class PipelineDagBuilder
{
    public static PipelineDag Build(PipelineGraph graph, Guid pipelineId)
    {
        PipelineRoles.RequireRolesAreBound(graph);

        var pipeline = graph.Thing(pipelineId)
            ?? throw new PipelineModelException($"Pipeline {pipelineId} not found in the loaded subgraph.");
        if (!graph.IsOf(pipeline, PipelineRoles.Pipeline))
            throw new PipelineModelException($"Thing {pipelineId} ('{pipeline.Name}') is not a pipeline.");

        var nodeThings = graph.OutgoingTargets(pipeline, ModelNames.Has)
            .Where(t => graph.IsOf(t, PipelineRoles.PipelineNode))
            .ToList();
        if (nodeThings.Count == 0)
            throw new PipelineModelException($"Pipeline '{pipeline.Name}' has no nodes.");

        var problems = new List<string>();
        var nodes = new List<DagNode>();
        var errorPort = ToPort(graph.Bound(PipelineRoles.ErrorPort)!);
        foreach (var nodeThing in nodeThings)
            if (Judged(problems, () => BuildNode(graph, nodeThing, errorPort, problems), null) is { } built)
                nodes.Add(built);
        var nodesById = nodes.ToDictionary(node => node.NodeId);

        // A wire leaving the pipeline, or touching a node that could not be read, is left out; the node's problem is
        // already listed.
        var wires = new List<DagWire>();
        foreach (var nodeThing in nodeThings)
        {
            if (!nodesById.TryGetValue(nodeThing.Id, out var from)) continue;

            foreach (var rel in graph.OutgoingByPredicateOf(nodeThing, PipelineRoles.PipelineWire))
                if (nodesById.TryGetValue(rel.TargetId, out var to))
                    problems.Add($"The wire from '{from.Name}' to '{to.Name}' is drawn as a relationship between the two nodes; "
                        + "a wire is a Thing the node it leaves has, related to the two ports it connects.");

            foreach (var (wire, target) in graph.HeldThingsOf(nodeThing, PipelineRoles.PipelineWire, PipelineRoles.PipelineNode))
                if (nodesById.TryGetValue(target.Id, out var to) && WireFrom(graph, wire, from, to, problems) is { } built)
                    wires.Add(built);
        }

        return new PipelineDag
        {
            PipelineId = pipelineId,
            Name = pipeline.Name,
            Published = pipeline.CarriesFlag(ModelNames.Published),
            Nodes = nodes,
            Wires = wires,
            Success = graph.Bound(PipelineRoles.Success)!.Id,
            Fail = graph.Bound(PipelineRoles.Fail)!.Id,
            ErrorPort = errorPort.Id,
            Problems = problems,
        };
    }

    // Reading on past a problem is what lets a refusal list every problem rather than the first.
    private static T Judged<T>(List<string> problems, Func<T> read, T unread)
    {
        try
        {
            return read();
        }
        catch (PipelineModelException ex)
        {
            problems.Add(ex.Message);
            return unread;
        }
    }

    // Both ends are judged before either refuses, so a wire wrong at both ends is listed with both reasons.
    private static DagWire? WireFrom(PipelineGraph graph, GraphThing wire, DagNode from, DagNode to, List<string> problems)
    {
        var named = $"The wire from '{from.Name}' to '{to.Name}'";
        var outPort = Judged(problems, () => ConnectedPort(graph, wire, PipelineRoles.InputConnector, from, output: true, named), null);
        var inPort = Judged(problems, () => ConnectedPort(graph, wire, PipelineRoles.OutputConnector, to, output: false, named), null);
        var takenOn = graph.OutgoingAlong(wire, PipelineRoles.TakenOn).Select(taken => taken.Id).ToHashSet();
        if (takenOn.Count == 0)
            problems.Add($"{named} is taken on nothing: it relates along {PipelineRoles.TakenOn.Word} to no result or decision, "
                + $"so no answer from '{from.Name}' could carry along it.");
        if (outPort is null || inPort is null || takenOn.Count == 0) return null;

        return new DagWire(new WireEnd(from, outPort), new WireEnd(to, inPort),
            wire.PropertyString(ModelNames.FromPath) ?? string.Empty,
            wire.PropertyString(ModelNames.ToPath) ?? string.Empty,
            wire.PropertyString(ModelNames.Transform) ?? string.Empty)
        {
            TakenOn = takenOn,
        };
    }

    private static DagPort ConnectedPort(PipelineGraph graph, GraphThing wire, Role connector, DagNode node, bool output, string named)
    {
        var reaching = output ? "takes its value from" : "delivers its value to";
        var connected = graph.OutgoingAlong(wire, connector).ToList();
        if (connected.Count == 0)
            throw new PipelineModelException($"{named} {reaching} no port: it relates to none along {connector.Word}.");
        if (connected.Count > 1)
            throw new PipelineModelException($"{named} relates to {connected.Count} ports along {connector.Word}; a wire {reaching} one.");

        return node.Ports.FirstOrDefault(port => port.Id == connected[0].Id && (output ? port.IsOutput : port.IsInput))
            ?? throw new PipelineModelException(
                $"{named} {reaching} '{connected[0].Name}', which is not {(output ? "an out" : "an in")} port of '{node.Name}'.");
    }

    private static DagNode BuildNode(PipelineGraph graph, GraphThing nodeThing, DagPort errorPort, List<string> problems)
    {
        // Boundary nodes bind no Connection/Service: they declare their own ports (has → Port) and
        // are an Input source (params → outputs) or an Output sink (inputs → run result).
        if (graph.IsOf(nodeThing, PipelineRoles.PipelineInput))
            return BuildBoundaryNode(graph, nodeThing, DagNodeKind.Input, problems);
        if (graph.IsOf(nodeThing, PipelineRoles.PipelineOutput))
            return BuildBoundaryNode(graph, nodeThing, DagNodeKind.Output, problems);
        if (graph.IsOf(nodeThing, PipelineRoles.PipelineTell))
            return BuildOrchestratorNode(graph, nodeThing, DagNodeKind.Tell, errorPort, problems);
        if (graph.IsOf(nodeThing, PipelineRoles.PipelineFollowOn))
            return BuildOrchestratorNode(graph, nodeThing, DagNodeKind.FollowOn, errorPort, problems);
        if (graph.IsOf(nodeThing, PipelineRoles.PipelineStop))
        {
            if (graph.Bound(PipelineRoles.RequestedBy) is null)
                problems.Add($"Node '{nodeThing.Name}' stops runs, but the model binds nothing to the role {PipelineRoles.RequestedBy.Word}, "
                    + "so its requests could not name the run that wrote them and would stop that run too.");
            return BuildOrchestratorNode(graph, nodeThing, DagNodeKind.Stop, errorPort, problems);
        }

        var connection = graph.OutgoingTargets(nodeThing, ModelNames.Has)
            .FirstOrDefault(t => graph.IsOf(t, PipelineRoles.Connection))
            ?? throw new PipelineModelException($"Node '{nodeThing.Name}' binds no service connection.");

        var subdomain = connection.PropertyString(ModelNames.Subdomain);
        if (string.IsNullOrWhiteSpace(subdomain))
            throw new PipelineModelException($"Connection '{connection.Name}' for node '{nodeThing.Name}' has no {ModelNames.Subdomain}.");

        var service = graph.OutgoingTargets(connection, ModelNames.Has)
            .FirstOrDefault(t => graph.IsOf(t, PipelineRoles.Service))
            ?? throw new PipelineModelException($"Connection '{connection.Name}' binds no service.");

        // The node's own ports come first so one of them wins a name clash with the service's.
        var ports = ResolvePortThings(graph, nodeThing).Concat(ResolvePortThings(graph, service))
            .Select(portThing => (Thing: portThing, Port: ToPort(portThing))).ToList();
        return new DagNode
        {
            NodeId = nodeThing.Id,
            Name = nodeThing.Name,
            Subdomain = subdomain!,
            Params = new Dictionary<string, JsonElement>(nodeThing.Properties, StringComparer.Ordinal),
            Ports = [.. ports.Select(p => p.Port), errorPort],
            ParameterBindings = ParseParameterBindings(nodeThing),
            Choices = Judged(problems, () => ChoicesOf(graph, nodeThing, ports), NoChoices),
            DecisionPaths = Judged(problems, () => DecisionPathsOf(graph, nodeThing), []),
            Waits = Judged(problems, () => WaitOf(graph, nodeThing, ports), null),
            Retries = Judged(problems, () => RetryPolicyOf(nodeThing), null),
            Rehearses = UpTheIsChain(graph, service).Any(kind => kind.CarriesFlag(ModelNames.Rehearses)),
            OnItemError = string.Equals(nodeThing.PropertyString(ModelNames.OnItemError), ModelNames.OnItemErrorContinue, StringComparison.OrdinalIgnoreCase)
                ? ModelNames.OnItemErrorContinue
                : ModelNames.OnItemErrorFail,
        };
    }

    // Each Thing the node chose is matched to the in port offering it; a choice no port offers refuses the
    // build, since the run could neither fill an input with it nor say which it meant.
    private static IReadOnlyDictionary<string, ChosenThing> ChoicesOf(
        PipelineGraph graph, GraphThing nodeThing, IReadOnlyList<(GraphThing Thing, DagPort Port)> ports)
    {
        var choices = new Dictionary<string, ChosenThing>(StringComparer.OrdinalIgnoreCase);
        foreach (var chosen in graph.OutgoingAlong(nodeThing, PipelineRoles.Choice))
        {
            var offering = ports.FirstOrDefault(p => p.Port.IsInput
                && graph.OutgoingAlong(p.Thing, PipelineRoles.Choice).Any(offered => offered.Id == chosen.Id));
            if (offering.Thing is null)
                throw new PipelineModelException($"Node '{nodeThing.Name}' chose '{chosen.Name}', which no in port of it offers.");
            if (choices.ContainsKey(offering.Port.PortName))
                throw new PipelineModelException($"Node '{nodeThing.Name}' chose two Things for its port '{offering.Port.PortName}'.");
            choices[offering.Port.PortName] = new ChosenThing(chosen.Id, chosen.Name);
        }
        return choices;
    }

    private static IReadOnlyList<DagDecisionPath> DecisionPathsOf(PipelineGraph graph, GraphThing nodeThing) =>
        graph.OutgoingTargets(nodeThing, ModelNames.Has)
            .Where(held => graph.IsOf(held, PipelineRoles.DecisionPath))
            .DistinctBy(path => path.Id)
            .Select(path => new DagDecisionPath(path.Id, path.Name, RuleOf(nodeThing, path), JsonSerializer.SerializeToElement(
                graph.OutgoingAlong(path, PipelineRoles.ComparesWith).Select(compared => new { id = compared.Id, name = compared.Name }))))
            .ToList();

    private static JsonataTransform? RuleOf(GraphThing nodeThing, GraphThing path)
    {
        var when = path.PropertyString(ModelNames.When);
        if (string.IsNullOrWhiteSpace(when)) return null;
        if (JsonataTransform.Validate(when) is { } error)
            throw new PipelineModelException($"'{nodeThing.Name}' has the choice '{path.Name}', whose rule does not compile: {error}");
        return new JsonataTransform(when);
    }

    private static DagWait? WaitOf(
        PipelineGraph graph, GraphThing nodeThing, IReadOnlyList<(GraphThing Thing, DagPort Port)> ports)
    {
        var states = graph.OutgoingAlong(nodeThing, PipelineRoles.CompletionRange).ToList();
        var through = graph.OutgoingAlong(nodeThing, PipelineRoles.AwaitsThrough).ToList();
        if (states.Count == 0 && through.Count == 0) return null;

        if (states.Count > 1)
            throw new PipelineModelException($"Node '{nodeThing.Name}' waits for two states, so no one answer could resume it.");
        if (through.Count > 1)
            throw new PipelineModelException($"Node '{nodeThing.Name}' waits through two ports, so no one Thing could be waited for.");
        if (through.Count == 0)
            throw new PipelineModelException($"Node '{nodeThing.Name}' waits for '{states[0].Name}' but names no in port whose Thing must enter it.");

        var portThing = through[0];
        var port = ports.FirstOrDefault(p => p.Thing.Id == portThing.Id).Port;
        var portName = port?.PortName ?? portThing.PropertyString(ModelNames.PortName) ?? portThing.Name;
        if (states.Count == 0)
            throw new PipelineModelException($"Node '{nodeThing.Name}' waits through its port '{portName}' but names no state for it to enter.");
        if (port is not { IsInput: true })
            throw new PipelineModelException($"Node '{nodeThing.Name}' waits through '{portName}', which is not an in port of it.");
        return new DagWait(states[0].Id, states[0].Name, port.PortName);
    }

    // A boundary node: ports are declared on the node itself (its own has → Port chain),
    // there is no subdomain to call, and its DagNodeKind tells the executor to seed from params
    // (Input) or collect into the run result (Output).
    private static DagNode BuildBoundaryNode(PipelineGraph graph, GraphThing nodeThing, DagNodeKind kind, List<string> problems)
    {
        if (kind == DagNodeKind.Output)
            Judged(problems, () => RefuseAnOutputNodeStandingForAnything(graph, nodeThing), unread: false);
        return new DagNode
        {
            NodeId = nodeThing.Id,
            Name = nodeThing.Name,
            Kind = kind,
            Subdomain = string.Empty,
            Params = new Dictionary<string, JsonElement>(nodeThing.Properties, StringComparer.Ordinal),
            Ports = ResolvePortThings(graph, nodeThing).Select(ToPort).ToList(),
            ParameterBindings = ParseParameterBindings(nodeThing),
            PassesWhen = kind == DagNodeKind.Output ? Judged(problems, () => OutputRuleOf(nodeThing), null) : null,
        };
    }

    private static JsonataTransform? OutputRuleOf(GraphThing output)
    {
        var when = output.PropertyString(ModelNames.When);
        if (string.IsNullOrWhiteSpace(when)) return null;
        if (JsonataTransform.Validate(when) is { } error)
            throw new PipelineModelException($"'{output.Name}' is the Output node, and its rule does not compile: {error}");
        return new JsonataTransform(when);
    }

    private static bool RefuseAnOutputNodeStandingForAnything(PipelineGraph graph, GraphThing output)
    {
        foreach (var stoodFor in graph.OutgoingAlong(output, PipelineRoles.StandsFor))
        {
            if (graph.IsOf(stoodFor, PipelineRoles.ExternalSystem))
                throw new PipelineModelException(
                    $"Node '{output.Name}' is the Output node and stands for '{stoodFor.Name}'; a tell node wired on to it tells a system.");
            if (graph.IsOf(stoodFor, PipelineRoles.Pipeline))
                throw new PipelineModelException(
                    $"Node '{output.Name}' is the Output node and stands for '{stoodFor.Name}'; a follow-on node wired on to it starts a pipeline.");
        }
        return true;
    }

    // Its own ports, as a boundary node declares them, and the error port, so a fail it answers can be carried on.
    private static DagNode BuildOrchestratorNode(PipelineGraph graph, GraphThing nodeThing, DagNodeKind kind, DagPort errorPort, List<string> problems) => new()
    {
        NodeId = nodeThing.Id,
        Name = nodeThing.Name,
        Kind = kind,
        Subdomain = string.Empty,
        Params = new Dictionary<string, JsonElement>(nodeThing.Properties, StringComparer.Ordinal),
        Ports = [.. ResolvePortThings(graph, nodeThing).Select(ToPort), errorPort],
        ParameterBindings = ParseParameterBindings(nodeThing),
        Tells = kind == DagNodeKind.Tell
            ? Judged(problems, () => TellingOf(graph, nodeThing)
                ?? throw new PipelineModelException($"Node '{nodeThing.Name}' is a tell node but stands for no external system."), null)
            : null,
        Starts = kind == DagNodeKind.FollowOn
            ? Judged(problems, () => PipelineReachedAlong(graph, nodeThing, PipelineRoles.StandsFor, "stands for")
                ?? throw new PipelineModelException($"Node '{nodeThing.Name}' is a follow-on node but stands for no pipeline."), null)
            : null,
    };

    // A node reaching its own pipeline is refused, since a run would start another of itself without end.
    private static Starting? PipelineReachedAlong(PipelineGraph graph, GraphThing node, Role role, string reaching)
    {
        var pipeline = graph.OutgoingAlong(node, role)
            .FirstOrDefault(target => graph.IsOf(target, PipelineRoles.Pipeline));
        if (pipeline is null) return null;

        var own = graph.RelationshipsInto(node, ModelNames.Has).Any(held => held.SubjectId == pipeline.Id);
        if (own)
            throw new PipelineModelException(
                $"Node '{node.Name}' {reaching} '{pipeline.Name}', the pipeline it is in, so a run would start another of itself without end.");
        return new Starting(pipeline.Id, pipeline.Name);
    }

    // A node with no attempts number has no policy and is called once, as before. The other numbers default to a
    // pause that neither grows nor spreads and a trying time of one day, so a node needs only the count.
    private static RetryPolicy? RetryPolicyOf(GraphThing nodeThing)
    {
        if (!int.TryParse(nodeThing.PropertyString(ModelNames.Attempts), out var attempts)) return null;
        if (attempts < 1)
            throw new PipelineModelException($"Node '{nodeThing.Name}' allows {attempts} attempts; a node that runs allows at least one.");

        double Number(string name, double fallback) =>
            double.TryParse(nodeThing.PropertyString(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
        var policy = new RetryPolicy(attempts, Number(ModelNames.PauseMinutes, 1), Number(ModelNames.PauseGrowth, 1), Number(ModelNames.PauseSpread, 0),
            Number(ModelNames.TryingMinutes, 24 * 60));
        if (policy.PauseMinutes < 0 || policy.PauseGrowth < 1 || policy.PauseSpread < 0 || policy.TryingMinutes <= 0)
            throw new PipelineModelException($"Node '{nodeThing.Name}' has a retry policy with a number out of range: the pause and the spread are at least 0, the growth at least 1, and the trying time above 0.");
        return policy;
    }

    // Refused at build rather than at the tell node, so a run that could never send its message does nothing first.
    private static Telling? TellingOf(PipelineGraph graph, GraphThing tellNode)
    {
        var system = graph.OutgoingAlong(tellNode, PipelineRoles.StandsFor)
            .FirstOrDefault(target => graph.IsOf(target, PipelineRoles.ExternalSystem));
        if (system is null) return null;

        var connection = graph.OutgoingAlong(system, PipelineRoles.ToldThrough)
            .FirstOrDefault(target => graph.IsOf(target, PipelineRoles.Connection))
            ?? throw new PipelineModelException(
                $"Node '{tellNode.Name}' stands for '{system.Name}', which names no connection it is told through, "
                + "so nothing could send it what reaches the node.");
        return new Telling(system.Id, system.Name, connection.Id);
    }

    // Parse the node's parameterBindings property — a JSON object mapping input-port name → the
    // run-parameter key that fills it. Malformed/absent → no bindings (never fails a build).
    private static IReadOnlyDictionary<string, string> ParseParameterBindings(GraphThing nodeThing)
    {
        var raw = nodeThing.PropertyString(ModelNames.ParameterBindings);
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

    private static readonly IReadOnlyDictionary<string, ChosenThing> NoChoices =
        new Dictionary<string, ChosenThing>();
    // Collect Port child-Things by walking the owner's is-chain — relationships do not
    // inherit through is, so ports resolve at read time at each level of the chain.
    private static IEnumerable<GraphThing> ResolvePortThings(PipelineGraph graph, GraphThing owner) =>
        UpTheIsChain(graph, owner).SelectMany(level => graph.OutgoingTargets(level, ModelNames.Has)
            .Where(t => graph.IsOf(t, PipelineRoles.Port)));

    // The Thing itself first, then each kind above it once.
    private static IEnumerable<GraphThing> UpTheIsChain(PipelineGraph graph, GraphThing owner)
    {
        var seen = new HashSet<Guid>();
        var stack = new Stack<GraphThing>();
        stack.Push(owner);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current.Id)) continue;
            yield return current;
            foreach (var parent in graph.OutgoingTargets(current, ModelNames.Is))
                stack.Push(parent);
        }
    }

    private static DagPort ToPort(GraphThing portThing) =>
        new(portThing.Id,
            portThing.PropertyString(ModelNames.PortName) ?? portThing.Name,
            portThing.PropertyString(ModelNames.Direction) ?? ModelNames.DirectionIn,
            portThing.PropertyString(ModelNames.PortType) ?? string.Empty,
            portThing.CarriesFlag(ModelNames.Required),
            portThing.CarriesFlag(ModelNames.Collection),
            portThing.CarriesFlag(ModelNames.Produces));
}
