using System.Text.Json;

namespace vos.ManagedMicroservice.Phloem.Model;

/// <summary>A resolved port on a node (from the bound service's <c>is</c>-chain).</summary>
public sealed record DagPort(string PortName, string Direction, string Type, bool Required)
{
    public bool IsInput => string.Equals(Direction, ModelNames.DirectionIn, StringComparison.OrdinalIgnoreCase);
    public bool IsOutput => string.Equals(Direction, ModelNames.DirectionOut, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A resolved DAG node: identity, the Connection subdomain Phloem forwards to, static params, and ports.</summary>
public sealed class DagNode
{
    public Guid NodeId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Subdomain { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, JsonElement> Params { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    public IReadOnlyList<DagPort> Ports { get; init; } = Array.Empty<DagPort>();

    public IEnumerable<DagPort> InputPorts => Ports.Where(p => p.IsInput);
    public IEnumerable<DagPort> OutputPorts => Ports.Where(p => p.IsOutput);

    public DagPort? Port(string portName) =>
        Ports.FirstOrDefault(p => string.Equals(p.PortName, portName, StringComparison.OrdinalIgnoreCase));
}

/// <summary>A wire: an output port on one node feeds an input port on another.</summary>
public sealed record DagWire(Guid FromNodeId, string FromPort, Guid ToNodeId, string ToPort);

/// <summary>The resolved pipeline DAG ready to validate and execute.</summary>
public sealed class PipelineDag
{
    public Guid PipelineId { get; init; }
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<DagNode> Nodes { get; init; } = Array.Empty<DagNode>();
    public IReadOnlyList<DagWire> Wires { get; init; } = Array.Empty<DagWire>();

    public DagNode? Node(Guid id) => Nodes.FirstOrDefault(n => n.NodeId == id);

    /// <summary>Wires feeding into the given node (its incoming edges).</summary>
    public IEnumerable<DagWire> WiresInto(Guid nodeId) => Wires.Where(w => w.ToNodeId == nodeId);
}
