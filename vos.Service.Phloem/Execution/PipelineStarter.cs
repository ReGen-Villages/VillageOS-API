using System.Text.Json;
using vos.Service.Phloem.Model;

namespace vos.Service.Phloem.Execution;

public sealed record SubjectReading(RunSubject? Subject, string? Refusal);

// Answers which pipeline a relationship the broker sends starts. A `runs` write names its pipeline as the target, and a
// state's call record names its connection as the target. Any other relationship was written along a predicate: a
// pipeline drawn from that predicate starts when the predicate names a connection along notifies, and otherwise the
// pipeline drawn from what the relationship reaches does.
public sealed class PipelineStarter
{
    private readonly IMyceliumGateway _gateway;

    public PipelineStarter(IMyceliumGateway gateway) => _gateway = gateway;

    public async Task<StartResolution> ResolveAsync(Guid targetId, Guid? relationshipId, Guid? subjectId, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid> arrivingRelatesTo = subjectId is { } subject ? await _gateway.RelatedFromAsync(subject, cancellationToken) : [];
        var graph = await _gateway.LoadStartSubgraphAsync(targetId, arrivingRelatesTo, cancellationToken);
        if (relationshipId is null || NamesWhatStarts(graph, targetId))
            return PipelineStart.Resolve(graph, targetId, arrivingRelatesTo) with
            {
                CallRecordId = graph.Thing(targetId) is { } target && graph.IsOf(target, PipelineRoles.Connection) ? relationshipId : null,
            };

        if (await _gateway.PredicateOfAsync(relationshipId.Value, cancellationToken) is { } predicateId)
        {
            var predicateGraph = await _gateway.LoadStartSubgraphAsync(predicateId, arrivingRelatesTo, cancellationToken);
            if (predicateGraph.Thing(predicateId) is { } predicate && NamesAConnection(predicateGraph, predicate)
                && PipelineStart.Resolve(predicateGraph, predicateId, arrivingRelatesTo) is { Started: true } fromThePredicate)
                return fromThePredicate;
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

    private static bool NamesAConnection(PipelineGraph graph, GraphThing predicate) =>
        graph.OutgoingAlong(predicate, PipelineRoles.NotifiedConnection).Any(called => graph.IsOf(called, PipelineRoles.Connection));

    private static bool NamesWhatStarts(PipelineGraph graph, Guid targetId) =>
        graph.Thing(targetId) is { } target
        && (graph.IsOf(target, PipelineRoles.Pipeline) || graph.IsOf(target, PipelineRoles.Connection));
}
