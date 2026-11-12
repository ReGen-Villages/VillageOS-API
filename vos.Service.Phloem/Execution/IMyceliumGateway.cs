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

    Task WriteRelationshipAsync(Guid subjectId, Guid predicateId, Guid targetId, CancellationToken cancellationToken);

    // Each Thing a Thing relates to along one predicate.
    Task<IReadOnlyList<RunSubject>> ReachedAlongAsync(Guid thingId, Guid predicateId, CancellationToken cancellationToken);

    // None when the model holds no such Thing.
    Task<RunSubject?> SubjectAsync(Guid thingId, CancellationToken cancellationToken);

    // The predicate a relationship was written along, or none when the model does not hold it.
    Task<Guid?> PredicateOfAsync(Guid relationshipId, CancellationToken cancellationToken);

    // A message the run leaves for an external system: written, held by the run, then related to the system
    // along the connection it is told through, which the broker delivers.
    Task SendMessageAsync(Guid runId, Telling telling, JsonElement payload, CancellationToken cancellationToken);

    // The subject, when the run has one, is related to the run along the predicate the model marks for it.
    // The request that started it, when the broker named one, is the identifier of its request-log entry.
    // The parameters are kept on the run as given, so a trace and a resumed run read what it was asked.
    // A run another run's end started reaches that run along the predicate the startingRun role binds, and a run a
    // person started again reaches the run it repeats along the predicate the repeatedRun role binds.
    // A refused run is written finished with its refusal, so a run nobody awaits still says in the model why it never ran.
    Task CreateRunAsync(Guid runId, Guid pipelineId, CancellationToken cancellationToken, RunSubject? subject = null, Guid? requestId = null,
        JsonElement? parameters = null, Guid? startedBy = null, string? refusal = null, bool rehearsal = false, Guid? repeats = null);

    // Upsert a NodeRun Thing and set its status — running before its service is called, then the terminal
    // status — on one Thing per key (deterministic id) so the SSE view sees a property change, not duplicate
    // Things. With index null this is the node's aggregate NodeRun (drives the
    // ring); with an index it's a per-item NodeRun of a fan-out, carrying index/total. A terminal status
    // carries the outputs, which a resumed run reads back from the model. A waiting status writes the watch's two
    // relationships in the same commit; any other status retracts them there, so a wait never outlives its node run.
    Task SetNodeRunStatusAsync(Guid runId, Guid nodeId, string nodeName, string status, string? error, CancellationToken cancellationToken, int? index = null, int total = 0,
        IReadOnlyDictionary<string, JsonElement>? outputs = null, Guid? decision = null, Watch? watch = null);

    // In one commit: the node run succeeds with its outputs, its watch is retracted and its run is running again.
    Task ResumeFromWaitAsync(Guid runId, Guid nodeId, string nodeName, IReadOnlyDictionary<string, JsonElement> outputs, Guid? decision, Watch watch,
        CancellationToken cancellationToken);

    // None unless the Thing is a node run a run holds.
    Task<HeldNodeRun?> NodeRunAsync(Guid thingId, CancellationToken cancellationToken);

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

    // A stop request a stop node writes, related to the run that wrote it and then to the Thing, which the broker sends
    // back as a stop.
    Task WriteStopRequestAsync(Guid runId, Guid thingId, string? reason, CancellationToken cancellationToken);

    // The reason a stop request carries, and the runs it stops: the target itself when it is a run, otherwise the
    // runs about it still running, waiting or trying, read off its run-subject relationships. The run a stop node's
    // request is requestedBy is never among them.
    Task<StopRequested> StopRequestedAsync(Guid stopRequestId, Guid targetId, CancellationToken cancellationToken);

    // Along the predicate the stoppedBy role binds; a model binding nothing to it records the reason alone.
    Task RelateStoppedByAsync(Guid runId, Guid stopRequestId, CancellationToken cancellationToken);

    Task<NodeCallResult> CallAsync(string subdomain, JsonElement envelope, CancellationToken cancellationToken);

    // Null when the model holds no such run. Reading a run's node runs back also tells the gateway they exist,
    // so a later status is written to the same Thing after a restart.
    Task<RunRecord?> ReadRunRecordAsync(Guid runId, CancellationToken cancellationToken);

    // Each waiting node run with the watch it holds and the state of the platform's call record answering it.
    Task<IReadOnlyList<HeldWait>> WaitingNodeRunsAsync(CancellationToken cancellationToken);

    // An attempt is a Thing the node run holds, and the node run's attempts count moves to the attempt's number.
    Task RecordAttemptAsync(Guid runId, Guid nodeId, int? index, AttemptRecord attempt, CancellationToken cancellationToken);

    Task<IReadOnlyList<UnfinishedNodeRun>> UnfinishedNodeRunsAsync(CancellationToken cancellationToken);

    // Every Thing of the archetype the cadence role binds, none when the model binds nothing to it.
    Task<IReadOnlyList<Cadence>> CadencesAsync(CancellationToken cancellationToken);

    Task SetLastDueAsync(Guid cadenceId, DateTimeOffset due, CancellationToken cancellationToken);
}
