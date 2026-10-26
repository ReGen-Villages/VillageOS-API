using System.Text.Json;
using vos.Service.Phloem.Model;

namespace vos.Service.Phloem.Execution;

public sealed record NodeCallResult(int StatusCode, string Body);

// Everything the executor needs from Mycelium, behind a seam so the orchestration logic is
// unit-testable without HTTP. The HTTP implementation is MyceliumGateway; run-persistence calls are
// best-effort (the executor never lets a persistence failure abort a run).
public interface IMyceliumGateway
{
    Task<PipelineGraph> LoadPipelineSubgraphAsync(Guid pipelineId, CancellationToken cancellationToken);

    // What the target of a relationship the broker sends is, with the start nodes standing for it and the pipelines
    // they belong to or it reaches, and each Thing the arriving Thing relates to with the start nodes standing for it —
    // enough for PipelineStart to say which pipeline it starts.
    Task<PipelineGraph> LoadStartSubgraphAsync(Guid targetId, IReadOnlyCollection<Guid> arrivingRelatesTo, CancellationToken cancellationToken);

    // Every Thing a Thing relates to, whatever the predicate.
    Task<IReadOnlyList<Guid>> RelatedFromAsync(Guid thingId, CancellationToken cancellationToken);

    // The predicate a relationship was written along, or none when the model does not hold it.
    Task<Guid?> PredicateOfAsync(Guid relationshipId, CancellationToken cancellationToken);

    // A message the run leaves for an external system: written, held by the run, then related to the system
    // along the connection it is told through, which the broker delivers.
    Task SendMessageAsync(Guid runId, Telling telling, JsonElement payload, CancellationToken cancellationToken);

    // The subject, when the run has one, is related to the run along the predicate the model marks for it.
    // The request that started it, when the broker named one, is the identifier of its request-log entry.
    // The parameters are kept on the run as given, so a trace and a resumed run read what it was asked.
    // A run another run's end started reaches that run along the predicate the startingRun role binds.
    // A refused run is written finished with its refusal, so a run nobody awaits still says in the model why it never ran.
    Task CreateRunAsync(Guid runId, Guid pipelineId, CancellationToken cancellationToken, RunSubject? subject = null, Guid? requestId = null,
        JsonElement? parameters = null, Guid? startedBy = null, string? refusal = null, bool rehearsal = false);

    // Upsert a NodeRun Thing and set its status — running before its service is called, then the terminal
    // status — on one Thing per key (deterministic id) so the SSE view sees a property change, not duplicate
    // Things. With index null this is the node's aggregate NodeRun (drives the
    // ring); with an index it's a per-item NodeRun of a fan-out, carrying index/total. A terminal status
    // carries the outputs, which a resumed run reads back from the model.
    Task SetNodeRunStatusAsync(Guid runId, Guid nodeId, string nodeName, string status, string? error, CancellationToken cancellationToken, int? index = null, int total = 0,
        IReadOnlyDictionary<string, JsonElement>? outputs = null);

    // The node run is related to every Thing the node answered under an out port marked as producing one,
    // along the predicate the model marks for it, so a trace is a read of the Thing's own relationships.
    Task RelateProducedAsync(Guid runId, Guid nodeId, int? index, IReadOnlyList<Guid> producedThingIds, CancellationToken cancellationToken);

    // The reason travels with a failure, so a run started by a state entry — one nobody awaits — still
    // says in the model why it failed.
    Task SetRunStatusAsync(Guid runId, string status, CancellationToken cancellationToken, string? error = null);

    // Store the pipeline's published result (the Output boundary node's collected inputs) on the
    // PipelineRun Thing, so it persists in the model and streams over SSE like any other property.
    Task SetRunResultAsync(Guid runId, JsonElement result, CancellationToken cancellationToken);

    // Whether a relationship the broker sends was written along the connection the runStopping role binds.
    Task<bool> IsStopAsync(Guid relationshipId, CancellationToken cancellationToken);

    // The reason a stop request carries, and the runs it stops: the target itself when it is a run, otherwise the
    // runs about it still running, waiting or trying, read off its run-subject relationships.
    Task<StopRequested> StopRequestedAsync(Guid stopRequestId, Guid targetId, CancellationToken cancellationToken);

    // Along the predicate the stoppedBy role binds; a model binding nothing to it records the reason alone.
    Task RelateStoppedByAsync(Guid runId, Guid stopRequestId, CancellationToken cancellationToken);

    // The platform answers a vigil already gone as not found, which is the outcome asked for.
    Task RetireVigilAsync(Guid vigilId, CancellationToken cancellationToken);

    Task<NodeCallResult> CallAsync(string subdomain, JsonElement envelope, CancellationToken cancellationToken);

    // Null when the model holds no such run. Reading a run's node runs back also tells the gateway they exist,
    // so a later status is written to the same Thing after a restart.
    Task<RunRecord?> ReadRunRecordAsync(Guid runId, CancellationToken cancellationToken);

    // Along the predicate the awaitedThing role binds, from the node run to the Thing it waits for.
    Task RelateAwaitedAsync(Guid runId, Guid nodeId, Guid awaitedId, CancellationToken cancellationToken);

    Task ForgetAwaitedAsync(Guid runId, Guid nodeId, CancellationToken cancellationToken);

    // A vigil over the Thing for the range, naming the connection the runResumption role binds.
    Task<VigilPlacement> PlaceVigilAsync(Guid subjectId, Guid rangeId, CancellationToken cancellationToken);

    // Along the predicate the heldVigil role binds, replacing any vigil the node run held before.
    Task RelateHeldVigilAsync(Guid runId, Guid nodeId, Guid vigilId, CancellationToken cancellationToken);

    Task<VigilHolder?> HolderOfVigilAsync(Guid vigilId, CancellationToken cancellationToken);

    Task<IReadOnlyList<HeldWait>> WaitingNodeRunsAsync(CancellationToken cancellationToken);

    // The platform deletes a vigil once it has been answered or its Thing is gone.
    Task<bool> IsVigilLiveAsync(Guid vigilId, CancellationToken cancellationToken);

    // An attempt is a Thing the node run holds, and the node run's attempts count moves to the attempt's number.
    Task RecordAttemptAsync(Guid runId, Guid nodeId, int? index, AttemptRecord attempt, CancellationToken cancellationToken);

    Task<IReadOnlyList<TryingNodeRun>> TryingNodeRunsAsync(CancellationToken cancellationToken);

    // Every Thing of the archetype the cadence role binds, none when the model binds nothing to it.
    Task<IReadOnlyList<Cadence>> CadencesAsync(CancellationToken cancellationToken);

    Task SetLastDueAsync(Guid cadenceId, DateTimeOffset due, CancellationToken cancellationToken);
}
