using System.Text.Json;
using vos.Service.Phloem.Model;

namespace vos.Service.Phloem.Execution;

public sealed record SubjectReading(RunSubject? Subject, string? Refusal);

// Answers which pipeline a relationship the broker sends starts, by reading from the model which connection it came
// through. A `runs` write names its pipeline as the target, and a state's call record names its connection
// as the target. Any other target is only what a write along a predicate joined its subject to, and there the
// connection is the predicate, read off the relationship itself.
public sealed class PipelineStarter
{
    private readonly IMyceliumGateway _gateway;

    public PipelineStarter(IMyceliumGateway gateway) => _gateway = gateway;

    public async Task<StartResolution> ResolveAsync(Guid targetId, Guid? relationshipId, Guid? subjectId, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid> arrivingRelatesTo = subjectId is { } subject ? await _gateway.RelatedFromAsync(subject, cancellationToken) : [];
        var graph = await _gateway.LoadStartSubgraphAsync(targetId, arrivingRelatesTo, cancellationToken);
        if (relationshipId is null || NamesWhatStarts(graph, targetId))
            return PipelineStart.Resolve(graph, targetId, arrivingRelatesTo);

        if (await _gateway.PredicateOfAsync(relationshipId.Value, cancellationToken) is { } predicateId)
        {
            var predicateGraph = await _gateway.LoadStartSubgraphAsync(predicateId, arrivingRelatesTo, cancellationToken);
            if (predicateGraph.Thing(predicateId) is { } predicate
                && predicateGraph.IsOf(predicate, PipelineRoles.Connection))
                return PipelineStart.Resolve(predicateGraph, predicateId, arrivingRelatesTo);
        }

        return PipelineStart.Resolve(graph, targetId, arrivingRelatesTo);
    }

    // A subject sent with a name is read again by its identifier, so the run carries the name the model holds.
    public async Task<SubjectReading> ReadSubjectAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty(PipelineExecutor.SubjectParam, out var sent)
            || sent.ValueKind == JsonValueKind.Null)
            return new SubjectReading(null, null);

        if (IdentifierIn(sent) is not { } thingId)
            return new SubjectReading(null, $"The subject {sent.GetRawText()} is not a Thing's identifier.");

        return await _gateway.SubjectAsync(thingId, cancellationToken) is { } held
            ? new SubjectReading(held, null)
            : new SubjectReading(null, $"The subject {thingId} names no Thing the model holds.");
    }

    private static Guid? IdentifierIn(JsonElement sent) => sent.ValueKind switch
    {
        JsonValueKind.String when Guid.TryParse(sent.GetString(), out var bare) => bare,
        JsonValueKind.Object when sent.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                                  && Guid.TryParse(id.GetString(), out var parsed) => parsed,
        _ => null,
    };

    private static bool NamesWhatStarts(PipelineGraph graph, Guid targetId) =>
        graph.Thing(targetId) is { } target
        && (graph.IsOf(target, PipelineRoles.Pipeline) || graph.IsOf(target, PipelineRoles.Connection));
}
