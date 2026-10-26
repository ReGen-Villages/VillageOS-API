namespace vos.Service.Phloem.Model;

// What a relationship's target turned out to start: the pipeline, or why none.
public sealed record StartResolution(Guid? PipelineId, string? Refusal)
{
    public bool Started => PipelineId is not null;
}

// Which pipeline a relationship posted to the orchestrator starts. The broker posts the same body for a
// `X runs Pipeline` relationship and for a Thing entering a watched state, and only the target tells them
// apart: the pipeline itself in the first, the connection that watches the state in the second. For any other
// target the start offers the target, the states it watches, and, of the Things the arriving Thing relates to, its
// message kind and external system. A published pipeline fits when everything its start node stands for is among those,
// and the one whose start node stands for the most of them starts, whatever order the pipelines are read in.
// Failing any fit, the target starts the pipeline it reaches along the predicate bound to starting one.
public static class PipelineStart
{
    private sealed record Claim(GraphThing Pipeline, IReadOnlySet<Guid> StandsFor);

    public static StartResolution Resolve(PipelineGraph graph, Guid targetId, IReadOnlyCollection<Guid> arrivingRelatesTo)
    {
        var target = graph.Thing(targetId);
        if (target is null)
            return new StartResolution(null, $"Nothing in the model has the identifier {targetId}.");

        if (graph.IsOf(target, PipelineRoles.Pipeline))
            return target.CarriesFlag(ModelNames.Published)
                ? new StartResolution(target.Id, null)
                : new StartResolution(null, $"'{target.Name}' is a draft, and the model starts only a published pipeline.");

        var offered = Offered(graph, target, arrivingRelatesTo);
        var fitting = Fitting(graph, offered);
        if (fitting.Count > 0)
            return MostSpecific(graph, fitting);

        return PipelineReachedFrom(graph, target) is { } reached
            ? new StartResolution(reached.Id, null)
            : new StartResolution(null,
                $"No published pipeline is drawn from '{target.Name}' for what this start offered ({Listed(offered)}): "
                + "no start node of one stands only for those, and it starts none.");
    }

    private static IReadOnlyList<GraphThing> Offered(PipelineGraph graph, GraphThing target, IReadOnlyCollection<Guid> arrivingRelatesTo)
    {
        var offered = new List<GraphThing> { target };
        offered.AddRange(graph.OutgoingAlong(target, PipelineRoles.StateWatch));
        offered.AddRange(arrivingRelatesTo.Select(graph.Thing).OfType<GraphThing>().Where(related =>
            graph.IsOf(related, PipelineRoles.MessageKind) || graph.IsOf(related, PipelineRoles.ExternalSystem)));
        return offered.DistinctBy(thing => thing.Id).ToList();
    }

    // A draft drawn from the same catalyst stands beside the pipeline it revises, and is never counted.
    private static List<Claim> Fitting(PipelineGraph graph, IReadOnlyList<GraphThing> offered)
    {
        var offeredIds = offered.Select(thing => thing.Id).ToHashSet();
        var read = new HashSet<Guid>();
        var fitting = new List<Claim>();
        foreach (var thing in offered)
        foreach (var node in graph.IncomingAlong(thing, PipelineRoles.StandsFor))
        {
            if (!read.Add(node.Id) || !graph.IsOf(node, PipelineRoles.PipelineInput)) continue;
            var pipeline = graph.IncomingSubjects(node, ModelNames.Has)
                .FirstOrDefault(holder => graph.IsOf(holder, PipelineRoles.Pipeline) && holder.CarriesFlag(ModelNames.Published));
            if (pipeline is null) continue;
            var standsFor = graph.RelationshipsAlong(node, PipelineRoles.StandsFor).Select(edge => edge.TargetId).ToHashSet();
            if (standsFor.IsSubsetOf(offeredIds))
                fitting.Add(new Claim(pipeline, standsFor));
        }
        return fitting;
    }

    private static StartResolution MostSpecific(PipelineGraph graph, List<Claim> fitting)
    {
        var most = fitting.Max(claim => claim.StandsFor.Count);
        var top = fitting.Where(claim => claim.StandsFor.Count == most).DistinctBy(claim => claim.Pipeline.Id).ToList();
        if (top.Count == 1)
            return new StartResolution(top[0].Pipeline.Id, null);

        var names = Listed(top.Select(claim => claim.Pipeline));
        var stood = top[0].StandsFor.Select(graph.Thing).OfType<GraphThing>();
        return new StartResolution(null, top.All(claim => claim.StandsFor.SetEquals(top[0].StandsFor))
            ? $"Pipelines {names} are published and their start nodes stand for exactly the same Things ({Listed(stood)}), so which "
              + "one starts would depend on the order they are read in. Make all but one a draft, or have one's start node stand for more."
            : $"Pipelines {names} all fit, and each start node stands for {most} of what this start offered, so {(top.Count == 2 ? "neither" : "none")} "
              + "is more specific. Have one's start node stand for more, or make one a draft.");
    }

    private static string Listed(IEnumerable<GraphThing> things) => string.Join(", ", things.Select(thing => $"'{thing.Name}'"));

    private static GraphThing? PipelineReachedFrom(PipelineGraph graph, GraphThing target) =>
        graph.OutgoingAlong(target, PipelineRoles.PipelineStart)
            .FirstOrDefault(reached => graph.IsOf(reached, PipelineRoles.Pipeline) && reached.CarriesFlag(ModelNames.Published));
}
