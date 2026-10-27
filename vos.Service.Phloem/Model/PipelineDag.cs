using System.Text.Json;

namespace vos.Service.Phloem.Model;

// A resolved port on a node: the bound service's, up its is-chain, and the node's own. Collection marks an
// input the node consumes as a list — the node fans out over it — or an output a fan-out gathers flat.
public sealed record DagPort(Guid Id, string PortName, string Direction, string Type, bool Required, bool Collection = false, bool Produces = false)
{
    public bool IsInput => string.Equals(Direction, ModelNames.DirectionIn, StringComparison.OrdinalIgnoreCase);
    public bool IsOutput => string.Equals(Direction, ModelNames.DirectionOut, StringComparison.OrdinalIgnoreCase);
    public bool IsNamed(string portName) => string.Equals(PortName, portName, StringComparison.OrdinalIgnoreCase);
}

// What a node does at execution: a callable Service node (the default), or a
// boundary node — an Input source (projects run params onto its output ports) or an
// Output sink (its collected inputs are the run's published result). Boundary nodes never
// call a service.
public enum DagNodeKind { Service, Input, Output }

// The external system an end node stands for, and the connection that system is told through.
public sealed record Telling(Guid SystemId, string SystemName, Guid ConnectionId);

// The pipeline an end node stands for: a run reaching the end starts a run of it with what reached the end.
public sealed record Starting(Guid PipelineId, string PipelineName);

// How a node keeps calling a service it cannot reach: how many attempts, the pause before the second in model
// minutes, the multiplier the pause grows by each attempt, the fraction of the pause added at random, and how long
// trying may last from the first attempt.
public sealed record RetryPolicy(int Attempts, double PauseMinutes, double PauseGrowth, double PauseSpread, double TryingMinutes)
{
    public TimeSpan PauseBefore(int attemptNumber, double spreadDraw) =>
        TimeSpan.FromMinutes(PauseMinutes * Math.Pow(PauseGrowth, attemptNumber - 2) * (1 + PauseSpread * spreadDraw));

    public TimeSpan TryingTime => TimeSpan.FromMinutes(TryingMinutes);
}

// A Thing the node chose among those an in port offers; the input is filled with it unless a wire or a
// parameter fills it.
public sealed record ChosenThing(Guid Id, string Name);

// What a node waits for once its service has answered: the Thing reaching PortName entering the range.
public sealed record DagWait(Guid RangeId, string RangeName, string PortName);

public sealed class DagNode
{
    public Guid NodeId { get; init; }
    public string Name { get; init; } = string.Empty;
    public DagNodeKind Kind { get; init; } = DagNodeKind.Service;
    public string Subdomain { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, JsonElement> Params { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    public IReadOnlyList<DagPort> Ports { get; init; } = Array.Empty<DagPort>();

    // Input-port → run-parameter-key bindings: when the node is called, each bound input is filled from the
    // run's parameters (an explicit wire into the same port takes precedence).
    public IReadOnlyDictionary<string, string> ParameterBindings { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // Per-item failure policy when fanning out: fail (fail-fast, default) or
    // continue (collect-partial — failed items become null holes, node is partial).
    public string OnItemError { get; init; } = ModelNames.OnItemErrorFail;

    // For an end node standing for an external system: who is sent what reaches it, and through what.
    public Telling? Tells { get; init; }

    public Starting? Starts { get; init; }

    public RetryPolicy? Retries { get; init; }

    public Starting? HandsOverTo { get; init; }

    public IReadOnlyDictionary<string, ChosenThing> Choices { get; init; } =
        new Dictionary<string, ChosenThing>(StringComparer.OrdinalIgnoreCase);

    // The decision paths its service declares, by the name an answer gives as its decision.
    public IReadOnlyDictionary<string, Guid> DecisionPaths { get; init; } =
        new Dictionary<string, Guid>(StringComparer.Ordinal);

    public DagWait? Waits { get; init; }

    public bool Rehearses { get; init; }

    public IEnumerable<DagPort> OutputPorts => Ports.Where(p => p.IsOutput);

    // A node with more than one is refused before it runs.
    public DagPort? CollectionInput => Ports.FirstOrDefault(p => p.IsInput && p.Collection);

    public DagPort? OutPort(string portName) => Ports.FirstOrDefault(p => p.IsOutput && p.IsNamed(portName));
}

public sealed record WireEnd(DagNode Node, DagPort Port);

// A wire: an output port on one node carries its value to an input port on another. FromPath and
// ToPath (both optional, empty = the whole payload) select a field of the upstream output
// and place it at a field of the downstream input, so several wires can compose one input. An optional
// Transform is a JSONata expression that reshapes the extracted value before placement. TakenOn holds the results
// and decisions whose answer the wire carries.
public sealed record DagWire(WireEnd From, WireEnd To, string FromPath = "", string ToPath = "", string Transform = "")
{
    public IReadOnlySet<Guid> TakenOn { get; init; } = new HashSet<Guid>();
}

public sealed class PipelineDag
{
    private readonly Lazy<Dictionary<Guid, DagNode>> _nodesById;
    private readonly Lazy<ILookup<Guid, DagWire>> _wiresInto;
    private readonly Lazy<ILookup<Guid, DagWire>> _wiresOutOf;

    public PipelineDag()
    {
        _nodesById = new(() => Nodes.ToDictionary(node => node.NodeId));
        _wiresInto = new(() => Wires.ToLookup(wire => wire.To.Node.NodeId));
        _wiresOutOf = new(() => Wires.ToLookup(wire => wire.From.Node.NodeId));
    }

    public Guid PipelineId { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool Published { get; init; }
    public IReadOnlyList<DagNode> Nodes { get; init; } = Array.Empty<DagNode>();
    public IReadOnlyList<DagWire> Wires { get; init; } = Array.Empty<DagWire>();
    public Guid Success { get; init; }
    public Guid Fail { get; init; }
    public Guid ErrorPort { get; init; }

    // What the build could not read, one sentence each; a node it could not read at all is left out of Nodes.
    public IReadOnlyList<string> Problems { get; init; } = Array.Empty<string>();

    public DagNode? Node(Guid id) => _nodesById.Value.GetValueOrDefault(id);

    public IEnumerable<DagWire> WiresInto(Guid nodeId) => _wiresInto.Value[nodeId];

    public IEnumerable<DagWire> WiresOutOf(Guid nodeId) => _wiresOutOf.Value[nodeId];
}
