using System.Text.Json;

namespace vos.ManagedMicroservice.Phloem.Model;

// A resolved port on a node (from the bound service's is-chain). Collection
// marks an input the node consumes as a list — the node fans out over it (#5648).
public sealed record DagPort(string PortName, string Direction, string Type, bool Required, bool Collection = false)
{
    public bool IsInput => string.Equals(Direction, ModelNames.DirectionIn, StringComparison.OrdinalIgnoreCase);
    public bool IsOutput => string.Equals(Direction, ModelNames.DirectionOut, StringComparison.OrdinalIgnoreCase);
}

// What a node does at execution: a dispatchable Service node (the default), or a
// boundary node — an Input source (projects run params onto its output ports) or an
// Output sink (its collected inputs are the run's published result). Boundary nodes never
// dispatch (#5873).
public enum DagNodeKind { Service, Input, Output }

// A resolved DAG node: identity, the Connection subdomain Phloem forwards to, static params, and ports.
public sealed class DagNode
{
    public Guid NodeId { get; init; }
    public string Name { get; init; } = string.Empty;
    public DagNodeKind Kind { get; init; } = DagNodeKind.Service;
    public string Subdomain { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, JsonElement> Params { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    public IReadOnlyList<DagPort> Ports { get; init; } = Array.Empty<DagPort>();

    // Input-port → run-param-key bindings (#5647): at dispatch each bound input is filled from the
    // run's params (an explicit wire into the same port takes precedence).
    public IReadOnlyDictionary<string, string> ParamBindings { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // Per-item failure policy when fanning out (#5648): fail (fail-fast, default) or
    // continue (collect-partial — failed items become null holes, node is partial).
    public string OnItemError { get; init; } = ModelNames.OnItemErrorFail;

    public IEnumerable<DagPort> InputPorts => Ports.Where(p => p.IsInput);
    public IEnumerable<DagPort> OutputPorts => Ports.Where(p => p.IsOutput);

    // The single collection input this node fans out over, if any (v1 supports exactly one).
    public DagPort? CollectionInput => Ports.FirstOrDefault(p => p.IsInput && p.Collection);

    public DagPort? Port(string portName) =>
        Ports.FirstOrDefault(p => string.Equals(p.PortName, portName, StringComparison.OrdinalIgnoreCase));
}

// A wire: an output port on one node feeds an input port on another. FromPath and
// ToPath (both optional, empty = the whole payload) select a field of the upstream output
// and place it at a field of the downstream input, so several wires can compose one input (#5874). An optional
// Transform is a JSONata expression that reshapes the extracted value before placement (#5875).
public sealed record DagWire(Guid FromNodeId, string FromPort, Guid ToNodeId, string ToPort, string FromPath = "", string ToPath = "", string Transform = "");

// The resolved pipeline DAG ready to validate and execute.
public sealed class PipelineDag
{
    public Guid PipelineId { get; init; }
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<DagNode> Nodes { get; init; } = Array.Empty<DagNode>();
    public IReadOnlyList<DagWire> Wires { get; init; } = Array.Empty<DagWire>();

    public DagNode? Node(Guid id) => Nodes.FirstOrDefault(n => n.NodeId == id);

    // Wires feeding into the given node (its incoming edges).
    public IEnumerable<DagWire> WiresInto(Guid nodeId) => Wires.Where(w => w.ToNodeId == nodeId);
}
