using vos.Service.Phloem.Model;

namespace vos.Service.Phloem.Execution;

// Answers which pipeline a dispatched relationship starts, by reading from the model which connection it came
// through. A `runs` write names its pipeline as the target, and a state's dispatch record names its connection
// as the target. Any other target is only what a write along a predicate joined its subject to, and there the
// connection is the predicate, read off the relationship itself.
public sealed class PipelineStarter
{
    private readonly IMyceliumGateway _gateway;

    public PipelineStarter(IMyceliumGateway gateway) => _gateway = gateway;

    public async Task<StartResolution> ResolveAsync(Guid targetId, Guid? relationshipId, CancellationToken cancellationToken)
    {
        var graph = await _gateway.LoadStartSubgraphAsync(targetId, cancellationToken);
        if (relationshipId is null || NamesWhatStarts(graph, targetId))
            return PipelineStart.Resolve(graph, targetId);

        if (await _gateway.PredicateOfAsync(relationshipId.Value, cancellationToken) is { } predicateId)
        {
            var predicateGraph = await _gateway.LoadStartSubgraphAsync(predicateId, cancellationToken);
            if (predicateGraph.Thing(predicateId) is { } predicate
                && predicateGraph.IsOfArchetypeCarrying(predicate, PipelineArchetypes.ConnectionFlag))
                return PipelineStart.Resolve(predicateGraph, predicateId);
        }

        return PipelineStart.Resolve(graph, targetId);
    }

    private static bool NamesWhatStarts(PipelineGraph graph, Guid targetId) =>
        graph.Thing(targetId) is { } target
        && (graph.IsOfArchetypeCarrying(target, PipelineArchetypes.PipelineFlag)
            || graph.IsOfArchetypeCarrying(target, PipelineArchetypes.ConnectionFlag));
}
