using vos.Service.Phloem.Model;

namespace vos.Service.Phloem.Execution;

// Answers which pipeline a dispatched relationship starts, by reading what its target is from the model.
public sealed class PipelineStarter
{
    private readonly IMyceliumGateway _gateway;

    public PipelineStarter(IMyceliumGateway gateway) => _gateway = gateway;

    public async Task<StartResolution> ResolveAsync(Guid targetId, CancellationToken cancellationToken)
    {
        var graph = await _gateway.LoadStartSubgraphAsync(targetId, cancellationToken);
        return PipelineStart.Resolve(graph, targetId);
    }
}
