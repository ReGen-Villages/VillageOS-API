using vos.Service.Phloem.Model;

namespace vos.Service.Phloem.Execution;

// Items meet on a wire with a transform, because a transform there sees the whole list.
public static class ItemWires
{
    public static bool Carries(PipelineDag dag, DagWire wire) =>
        wire.From.Node is { Kind: DagNodeKind.Service, CollectionInput: not null }
        && !wire.From.Port.Collection
        && wire.To.Node is { Kind: DagNodeKind.Service, CollectionInput: { } input }
        && input.Id == wire.To.Port.Id
        && string.IsNullOrEmpty(wire.Transform)
        && !wire.To.Node.Choices.ContainsKey(input.PortName)
        && !wire.To.Node.ParameterBindings.ContainsKey(input.PortName)
        && dag.WiresInto(wire.To.Node.NodeId).Count(other => other.To.Port.Id == input.Id) == 1;
}
