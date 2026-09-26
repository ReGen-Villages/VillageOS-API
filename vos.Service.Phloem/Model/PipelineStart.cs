namespace vos.Service.Phloem.Model;

// What a relationship's target turned out to start: the pipeline, or why none.
public sealed record StartResolution(Guid? PipelineId, string? Refusal)
{
    public bool Started => PipelineId is not null;
}

// Which pipeline a relationship posted to the orchestrator starts. The broker posts the same body for a
// `X runs Pipeline` relationship and for a Thing entering a watched state, and only the target tells them
// apart: the pipeline itself in the first, the connection that watches the state in the second. For a
// connection the pipeline is the one drawn from it — the one whose start node stands for it, or for the
// state it watches — or, failing that, the one the connection reaches along the predicate marked as
// starting a pipeline.
public static class PipelineStart
{
    public static StartResolution Resolve(PipelineGraph graph, Guid targetId)
    {
        var target = graph.Thing(targetId);
        if (target is null)
            return new StartResolution(null, $"Nothing in the model has the identifier {targetId}.");

        if (graph.IsOfArchetypeCarrying(target, PipelineArchetypes.PipelineFlag))
            return new StartResolution(target.Id, null);

        var drawnFrom = PipelineWhoseStartNodeStandsFor(graph, target)
            ?? graph.OutgoingAlongPredicateMarked(target, PipelinePredicates.StateWatchFlag)
                .Select(state => PipelineWhoseStartNodeStandsFor(graph, state))
                .FirstOrDefault(pipeline => pipeline is not null)
            ?? PipelineReachedFrom(graph, target);
        return drawnFrom is null
            ? new StartResolution(null,
                $"No pipeline is drawn from '{target.Name}': no start node stands for it or for a state it watches, and it starts none.")
            : new StartResolution(drawnFrom.Id, null);
    }

    private static GraphThing? PipelineWhoseStartNodeStandsFor(PipelineGraph graph, GraphThing target)
    {
        foreach (var node in graph.IncomingAlongPredicateMarked(target, PipelinePredicates.StandsForFlag))
        {
            if (!graph.IsOfArchetypeCarrying(node, PipelineArchetypes.PipelineInputFlag)) continue;
            var pipeline = graph.IncomingSubjects(node, ModelNames.Has)
                .FirstOrDefault(holder => graph.IsOfArchetypeCarrying(holder, PipelineArchetypes.PipelineFlag));
            if (pipeline is not null) return pipeline;
        }
        return null;
    }

    private static GraphThing? PipelineReachedFrom(PipelineGraph graph, GraphThing target) =>
        graph.OutgoingAlongPredicateMarked(target, PipelinePredicates.PipelineStartFlag)
            .FirstOrDefault(reached => graph.IsOfArchetypeCarrying(reached, PipelineArchetypes.PipelineFlag));
}
