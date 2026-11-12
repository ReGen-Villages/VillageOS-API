using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Shared;
using vos.Service.Shared.Subscriptions;
using vos.Service.Shared.Roles;

namespace vos.Service.Phloem.Services;

// The HTTP implementation of IMyceliumGateway over Mycelium's API: loads the pipeline
// subgraph via a subscription snapshot, persists run state through the thing/relationship API, and calls
// nodes through the endpoint-forward route. Also registers Phloem as a managed microservice.
public sealed class MyceliumGateway : MyceliumClientBase, IMyceliumGateway, IAsyncDisposable
{
    // Everything this gateway records about a run is text, so one type name serves every property it writes.
    private const string PropertyType = "vos.String";

    private readonly ConcurrentDictionary<string, Guid> _thingIdByName = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, Guid> _boundByRole = new();
    private readonly ConcurrentDictionary<Guid, byte> _createdNodeRuns = new();
    private readonly HeldCadences _cadences = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly object _followLock = new();
    private Task? _followingCadences;
    private int _disposed;

    public TimeSpan CadenceStreamRetry { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan DisposeWait { get; init; } = TimeSpan.FromSeconds(5);

    public MyceliumGateway(IHttpClientFactory httpClientFactory, ILogger<MyceliumGateway> logger, string myceliumUrl, LaunchToken? launchToken = null, string? apiKey = null)
        : base(httpClientFactory, logger, myceliumUrl, launchToken, apiKey: apiKey) { }

    public Task<bool> RegisterAsync(int port) => RegisterAsync(port, "Phloem", "endpoint-service");

    // A role is read off the drawing only when its role Thing is in it.
    private static readonly IReadOnlyList<Role> DrawingRoles =
        [.. PipelineRoles.DagRoles, PipelineRoles.ExternalSystem, PipelineRoles.StandsFor, PipelineRoles.ToldThrough, PipelineRoles.Choice,
         PipelineRoles.CompletionRange, PipelineRoles.AwaitsThrough, PipelineRoles.PipelineTell, PipelineRoles.PipelineFollowOn,
         PipelineRoles.PipelineStop, PipelineRoles.RequestedBy, PipelineRoles.PipelineWait, PipelineRoles.ReachedAlong, PipelineRoles.PipelineRelate];

    private static readonly IReadOnlyList<Role> StartRoles =
    [
        PipelineRoles.Pipeline, PipelineRoles.PipelineInput, PipelineRoles.Connection,
        PipelineRoles.StateWatch, PipelineRoles.StandsFor, PipelineRoles.PipelineStart,
        PipelineRoles.MessageKind, PipelineRoles.ExternalSystem,
    ];

    public async Task<PipelineGraph> LoadPipelineSubgraphAsync(Guid pipelineId, CancellationToken cancellationToken)
    {
        // The predicate Things and the role Things are asked for outright: includeRelationships does not pull predicate
        // Things, and a role missing from the snapshot has to mean the model binds it to nothing. The rules run in
        // order: the `has` walk, then one `is` step for the kind a service or node is, then one `has` step for that
        // kind's ports, which the walk from the pipeline never reaches because includeIsAncestors closes over `is`
        // only after the traversal.
        // Only outgoing relationships travel: every edge the drawing is read along runs outward from a Thing in the
        // answer, and without the rule the broker also returns every `is` written to an ancestor such as Connection.
        var selector = new
        {
            ids = DrawingRoles.Select(role => role.Id).Prepend(pipelineId).ToArray(),
            names = new[] { ModelNames.Is, ModelNames.Has, ModelNames.Binds },
            traverse = new object[]
            {
                new { predicate = ModelNames.Has, direction = "outgoing", depth = 8 },
                new { predicate = ModelNames.Is, direction = "outgoing", depth = 1 },
                new { predicate = ModelNames.Has, direction = "outgoing", depth = 1 },
                new { predicate = ModelNames.Binds, direction = "outgoing", depth = 1 },
                new { predicateRole = PipelineRoles.InputConnector.Id, direction = "outgoing", depth = 1 },
                new { predicateRole = PipelineRoles.OutputConnector.Id, direction = "outgoing", depth = 1 },
                new { predicateRole = PipelineRoles.StandsFor.Id, direction = "outgoing", depth = 1 },
                new { predicateRole = PipelineRoles.ToldThrough.Id, direction = "outgoing", depth = 1 },
                new { predicateRole = PipelineRoles.Choice.Id, direction = "outgoing", depth = 1 },
                new { predicateRole = PipelineRoles.CompletionRange.Id, direction = "outgoing", depth = 1 },
                new { predicateRole = PipelineRoles.ComparesWith.Id, direction = "outgoing", depth = 1 },
                new { predicateRole = PipelineRoles.ReachedAlong.Id, direction = "outgoing", depth = 1 },
                new { predicate = ModelNames.Has, direction = "outgoing", depth = 1 },
            },
            includeIsAncestors = true,
            includeRelationships = true,
            relationships = new object[] { new { direction = "outgoing" } },
        };

        return await LoadSnapshotAsync(selector, TimeSpan.FromSeconds(30), cancellationToken);
    }

    public async Task<PipelineGraph> LoadStartSubgraphAsync(Guid targetId, IReadOnlyCollection<Guid> arrivingRelatesTo, CancellationToken cancellationToken)
    {
        // The target and what tells it apart: its `is` chain, the state it watches, the start nodes standing
        // for either and the pipelines holding them, and any pipeline it reaches along the start role. The role
        // Things come with what each binds so the graph can tell what plays each role, and the traversals run in
        // order, so the watched state is reached before the nodes standing for it.
        // Relationship rules keep the read to those edges: a state connection gains one call record per
        // Thing that ever entered its state, and the wider reading would carry every one on every start.
        // Every stands-for edge out of a start node is read, so a node standing for a Thing the read leaves out
        // is not taken to fit.
        var selector = new
        {
            ids = StartRoles.Select(role => role.Id).Prepend(targetId).Concat(arrivingRelatesTo).ToArray(),
            names = new[] { ModelNames.Is, ModelNames.Has, ModelNames.Binds },
            traverse = new object[]
            {
                new { predicate = ModelNames.Binds, direction = "outgoing", depth = 1 },
                new { predicateRole = PipelineRoles.StateWatch.Id, direction = "outgoing", depth = 1 },
                new { predicateRole = PipelineRoles.StandsFor.Id, direction = "incoming", depth = 1 },
                new { predicate = ModelNames.Has, direction = "incoming", depth = 1 },
                new { predicateRole = PipelineRoles.PipelineStart.Id, direction = "outgoing", depth = 1 },
            },
            includeIsAncestors = true,
            includeRelationships = true,
            relationships = new object[]
            {
                new { predicate = ModelNames.Is, direction = "outgoing" },
                new { predicate = ModelNames.Binds, direction = "outgoing" },
                new { predicateRole = PipelineRoles.StateWatch.Id, direction = "outgoing" },
                new { predicateRole = PipelineRoles.StandsFor.Id, direction = "incoming" },
                new { predicateRole = PipelineRoles.StandsFor.Id, direction = "outgoing" },
                new { predicate = ModelNames.Has, direction = "incoming" },
                new { predicateRole = PipelineRoles.PipelineStart.Id, direction = "outgoing" },
            },
        };

        return await LoadSnapshotAsync(selector, TimeSpan.FromSeconds(15), cancellationToken);
    }

    // The relationships out of a Thing are what it was written with, so they stay few however much is later related to it.
    public async Task<IReadOnlyList<Guid>> RelatedFromAsync(Guid thingId, CancellationToken cancellationToken)
    {
        var selector = new
        {
            ids = new[] { thingId },
            includeIsAncestors = false,
            includeRelationships = true,
            relationships = new object[] { new { direction = "outgoing" } },
        };

        var graph = await LoadSnapshotAsync(selector, TimeSpan.FromSeconds(15), cancellationToken);
        return graph.Thing(thingId) is { } thing ? graph.TargetIdsFrom(thing).ToList() : [];
    }

    public async Task<IReadOnlyList<RunSubject>> ReachedAlongAsync(Guid thingId, Guid predicateId, CancellationToken cancellationToken)
    {
        var from = await LoadSnapshotAsync(new
        {
            ids = new[] { thingId },
            includeIsAncestors = false,
            includeRelationships = true,
            relationships = new object[] { new { direction = "outgoing" } },
        }, TimeSpan.FromSeconds(15), cancellationToken);
        if (from.Thing(thingId) is not { } thing)
            throw new InvalidOperationException($"the model holds no Thing {thingId}");

        var reachedIds = from.TargetIdsFromAlong(thing, predicateId).Distinct().ToArray();
        if (reachedIds.Length == 0) return [];

        var reached = await LoadSnapshotAsync(new { ids = reachedIds, includeIsAncestors = false, includeRelationships = false },
            TimeSpan.FromSeconds(15), cancellationToken);
        return reachedIds.Select(id => new RunSubject(id, reached.Thing(id)?.Name ?? string.Empty)).ToList();
    }

    public async Task CreateRunAsync(
        Guid runId, Guid pipelineId, CancellationToken cancellationToken, RunSubject? subject = null, Guid? requestId = null,
        JsonElement? parameters = null, Guid? startedBy = null, string? refusal = null, bool rehearsal = false, Guid? repeats = null)
    {
        // The result, the error and the finish are declared here with no value. Each is known only once the
        // run ends, and the property route sets a property the Thing already carries — so a run given one
        // only at the end is a run that can never be given one.
        var now = DateTime.UtcNow.ToString("o");
        var properties = new Dictionary<string, string?>
        {
            [ModelNames.Status] = refusal is null ? RunStatus.Running : RunStatus.Refused,
            ["pipelineId"] = pipelineId.ToString(),
            [ModelNames.StartedUtc] = now,
            [ModelNames.FinishedUtc] = refusal is null ? null : now,
            [ModelNames.Parameters] = parameters is { ValueKind: not JsonValueKind.Undefined } given ? given.GetRawText() : null,
            [ModelNames.Result] = null,
            [ModelNames.Error] = refusal,
            [ModelNames.Rehearsal] = rehearsal ? "true" : "false",
        };
        if (requestId is { } request) properties[ModelNames.RequestId] = request.ToString();
        var runKind = await BoundAsync(PipelineRoles.PipelineRun, cancellationToken);
        var relationships = new List<(Guid, Guid, Guid)>
        {
            (runId, await ResolveByNameAsync(ModelNames.Is, cancellationToken), runKind),
            (runId, await ResolveByNameAsync(ModelNames.Of, cancellationToken), pipelineId),
        };
        if (refusal is null) relationships.AddRange(await HoldingAsync(runId, cancellationToken));
        await WriteThingInOneFragmentAsync(runId, RunName(runId), properties, relationships, cancellationToken);

        if (startedBy is { } starter) await RelateIfBoundAsync(runId, PipelineRoles.StartingRun, starter, cancellationToken);
        if (repeats is { } repeated) await RelateIfBoundAsync(runId, PipelineRoles.RepeatedRun, repeated, cancellationToken);
        if (subject is { } about) await RelateIfBoundAsync(runId, PipelineRoles.RunSubject, about.Id, cancellationToken);
    }

    // A run is recorded without the relationship rather than refused, since the run has already begun.
    private async Task RelateIfBoundAsync(Guid runId, Role role, Guid targetId, CancellationToken cancellationToken)
    {
        var predicateId = await TryBoundAsync(role, cancellationToken);
        if (predicateId is null)
        {
            Logger.LogWarning("The model binds nothing to the role {Role}; run {RunId} is recorded without its relationship to {TargetId}",
                role.Word, runId, targetId);
            return;
        }
        await RelateAsync(runId, predicateId.Value, targetId, cancellationToken);
    }

    public async Task SetNodeRunStatusAsync(Guid runId, Guid nodeId, string nodeName, string status, string? error, CancellationToken cancellationToken, int? index = null, int total = 0,
        IReadOnlyDictionary<string, JsonElement>? outputs = null, Guid? decision = null, Watch? watch = null)
    {
        var nodeRunId = index is int i ? DeterministicGuid(runId, nodeId, i) : DeterministicGuid(runId, nodeId);
        var (watched, retracted) = await WatchEdgesAsync(nodeRunId, status, watch, cancellationToken);
        if (_createdNodeRuns.TryAdd(nodeRunId, 0))
        {
            // First status for this NodeRun — create the Thing and wire it into the run, declaring every
            // property a later transition writes.
            var now = DateTime.UtcNow.ToString("o");
            var properties = new Dictionary<string, string?>
            {
                [ModelNames.Status] = status,
                [ModelNames.NodeId] = nodeId.ToString(),
                [ModelNames.Error] = error,
                [ModelNames.StartedUtc] = now,
                [ModelNames.FinishedUtc] = RunStatus.IsFinished(status) ? now : null,
                [ModelNames.Outputs] = outputs is null ? null : OutputsText(outputs),
                [ModelNames.Attempts] = null,
                [ModelNames.Decision] = decision?.ToString(),
            };
            if (index is int idx)
            {
                properties[ModelNames.Index] = idx.ToString();
                properties[ModelNames.Total] = total.ToString();
            }
            var nodeRunKind = await BoundAsync(PipelineRoles.NodeRun, cancellationToken);
            await WriteFragmentAsync(NodeRunName(nodeName, index), [(nodeRunId, NodeRunName(nodeName, index), properties)],
            [
                (nodeRunId, await ResolveByNameAsync(ModelNames.Is, cancellationToken), nodeRunKind),
                (runId, await ResolveByNameAsync(ModelNames.Has, cancellationToken), nodeRunId),
                .. watched,
            ], retracted, cancellationToken);
            return;
        }

        await WriteFragmentAsync(NodeRunName(nodeName, index), [(nodeRunId, NodeRunName(nodeName, index), ChangedOnANodeRun(status, error, outputs, decision))],
            watched, retracted, cancellationToken);
    }

    // Changed on the same Thing, so the view watching it sees each property change rather than a new node.
    private static Dictionary<string, string?> ChangedOnANodeRun(string status, string? error, IReadOnlyDictionary<string, JsonElement>? outputs, Guid? decision)
    {
        var changed = new Dictionary<string, string?> { [ModelNames.Status] = status };
        if (error != null) changed[ModelNames.Error] = error;
        if (RunStatus.IsFinished(status)) changed[ModelNames.FinishedUtc] = DateTime.UtcNow.ToString("o");
        if (outputs is not null) changed[ModelNames.Outputs] = OutputsText(outputs);
        if (decision is { } decided) changed[ModelNames.Decision] = decided.ToString();
        return changed;
    }

    private async Task<(IReadOnlyList<(Guid Subject, Guid Predicate, Guid Target)> Written, IReadOnlyList<(Guid Subject, Guid Predicate, Guid Target)> Retracted)>
        WatchEdgesAsync(Guid nodeRunId, string status, Watch? watch, CancellationToken cancellationToken)
    {
        if (watch is null) return ([], []);
        var edges = new List<(Guid Subject, Guid Predicate, Guid Target)>();
        if (watch.AwaitedId is { } awaited) edges.Add((nodeRunId, await BoundAsync(PipelineRoles.AwaitedThing, cancellationToken), awaited));
        if (watch.RangeId is { } range) edges.Add((nodeRunId, await BoundAsync(PipelineRoles.StateWatch, cancellationToken), range));
        return status == RunStatus.Waiting ? (edges, []) : ([], edges);
    }

    public async Task ResumeFromWaitAsync(Guid runId, Guid nodeId, string nodeName, IReadOnlyDictionary<string, JsonElement> outputs, Guid? decision, Watch watch,
        CancellationToken cancellationToken)
    {
        var nodeRunId = DeterministicGuid(runId, nodeId);
        var (_, retracted) = await WatchEdgesAsync(nodeRunId, RunStatus.Succeeded, watch, cancellationToken);
        await WriteFragmentAsync(NodeRunName(nodeName, null),
        [
            (nodeRunId, NodeRunName(nodeName, null), ChangedOnANodeRun(RunStatus.Succeeded, null, outputs, decision)),
            (runId, RunName(runId), new Dictionary<string, string?> { [ModelNames.Status] = RunStatus.Running }),
        ], await HoldingAsync(runId, cancellationToken), retracted, cancellationToken);
    }

    public async Task<HeldNodeRun?> NodeRunAsync(Guid thingId, CancellationToken cancellationToken)
    {
        var selector = new
        {
            ids = new[] { thingId, PipelineRoles.NodeRun.Id },
            names = new[] { ModelNames.Is, ModelNames.Binds, ModelNames.Has },
            traverse = new object[]
            {
                new { predicate = ModelNames.Binds, direction = "outgoing", depth = 1 },
                new { predicate = ModelNames.Has, direction = "incoming", depth = 1 },
            },
            includeIsAncestors = false,
            includeRelationships = true,
            relationships = new object[]
            {
                new { predicate = ModelNames.Is, direction = "outgoing" },
                new { predicate = ModelNames.Binds, direction = "outgoing" },
                new { predicate = ModelNames.Has, direction = "incoming" },
            },
        };
        var graph = await LoadSnapshotAsync(selector, TimeSpan.FromSeconds(15), cancellationToken);
        if (graph.Thing(thingId) is not { } nodeRun || !graph.IsOf(nodeRun, PipelineRoles.NodeRun)
            || !Guid.TryParse(nodeRun.PropertyString(ModelNames.NodeId), out var nodeId)
            || graph.RelationshipsInto(nodeRun, ModelNames.Has).FirstOrDefault() is not { } held)
            return null;
        return new HeldNodeRun(held.SubjectId, nodeId);
    }

    private static string NodeRunName(string nodeName, int? index) =>
        index is int item ? $"NodeRun {nodeName} #{item}" : $"NodeRun {nodeName}";

    private static string RunName(Guid runId) => $"PipelineRun {runId:N}";

    private static string OutputsText(IReadOnlyDictionary<string, JsonElement> outputs) =>
        JsonSerializer.Serialize(outputs);

    // The broker takes an edge it already holds as nothing new, so holding the run travels in the status fragment at
    // no extra call.
    public async Task SetRunStatusAsync(Guid runId, string status, CancellationToken cancellationToken, string? error = null)
    {
        var changed = new Dictionary<string, string?> { [ModelNames.Status] = status };
        if (error != null) changed[ModelNames.Error] = error;
        var finished = RunStatus.IsFinished(status);
        if (finished) changed[ModelNames.FinishedUtc] = DateTime.UtcNow.ToString("o");
        await WriteThingInOneFragmentAsync(runId, RunName(runId), changed, finished ? [] : await HoldingAsync(runId, cancellationToken), cancellationToken);
        if (finished) await ReleaseAsync(runId, cancellationToken);
    }

    private async Task<IReadOnlyList<(Guid Subject, Guid Predicate, Guid Target)>> HoldingAsync(Guid runId, CancellationToken cancellationToken)
    {
        if (await TryBoundAsync(PipelineRoles.Orchestrator, cancellationToken) is not { } orchestrator)
        {
            Logger.LogWarning("The model binds nothing to the role {Role}; run {RunId} is recorded unheld, so a restart cannot recover it",
                PipelineRoles.Orchestrator.Word, runId);
            return [];
        }
        return [(orchestrator, await ResolveByNameAsync(ModelNames.Has, cancellationToken), runId)];
    }

    // The broker names the edge, so it has to be found among the orchestrator's relationships before it can be deleted.
    private async Task ReleaseAsync(Guid runId, CancellationToken cancellationToken)
    {
        if (await TryBoundAsync(PipelineRoles.Orchestrator, cancellationToken) is not { } orchestrator) return;
        var selector = new
        {
            ids = new[] { orchestrator },
            names = new[] { ModelNames.Has },
            includeIsAncestors = false,
            includeRelationships = true,
            relationships = new object[] { new { predicate = ModelNames.Has, direction = "outgoing" } },
        };
        var graph = await LoadSnapshotAsync(selector, TimeSpan.FromSeconds(15), cancellationToken);
        if (graph.Thing(orchestrator) is not { } holder) return;

        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(15));
        foreach (var held in graph.RelationshipsFrom(holder, ModelNames.Has).Where(held => held.TargetId == runId).ToList())
        {
            var response = await client.DeleteAsync($"{MyceliumUrl}/api/relationships/{held.Id}", cancellationToken);
            if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
        }
    }

    public async Task<RunRecord?> ReadRunRecordAsync(Guid runId, CancellationToken cancellationToken)
    {
        var selector = new
        {
            ids = new[] { runId, PipelineRoles.RepeatedRun.Id, PipelineRoles.AwaitedThing.Id, PipelineRoles.StateWatch.Id },
            names = new[] { ModelNames.Has, ModelNames.Of, ModelNames.Binds },
            traverse = new object[]
            {
                new { predicate = ModelNames.Has, direction = "outgoing", depth = 2 },
                new { predicate = ModelNames.Of, direction = "outgoing", depth = 1 },
                new { predicate = ModelNames.Binds, direction = "outgoing", depth = 1 },
                new { predicateRole = PipelineRoles.RepeatedRun.Id, direction = "incoming", depth = 1 },
            },
            includeIsAncestors = false,
            includeRelationships = true,
            relationships = new object[]
            {
                new { predicate = ModelNames.Has, direction = "outgoing" },
                new { predicate = ModelNames.Of, direction = "outgoing" },
                new { predicate = ModelNames.Binds, direction = "outgoing" },
                new { predicateRole = PipelineRoles.RepeatedRun.Id, direction = "incoming" },
                new { predicateRole = PipelineRoles.AwaitedThing.Id, direction = "outgoing" },
                new { predicateRole = PipelineRoles.StateWatch.Id, direction = "outgoing" },
            },
        };
        var graph = await LoadSnapshotAsync(selector, TimeSpan.FromSeconds(15), cancellationToken);
        if (graph.Thing(runId) is not { } run || graph.OutgoingTargets(run, ModelNames.Of).FirstOrDefault() is not { } pipeline)
            return null;
        var repeatedBy = graph.IncomingAlong(run, PipelineRoles.RepeatedRun).FirstOrDefault()?.Id;

        var nodes = new Dictionary<Guid, NodeRecord>();
        var items = new Dictionary<Guid, SortedList<int, NodeRecord>>();
        foreach (var held in graph.OutgoingTargets(run, ModelNames.Has))
        {
            if (!Guid.TryParse(held.PropertyString(ModelNames.NodeId), out var nodeId)) continue;
            _createdNodeRuns.TryAdd(held.Id, 0);
            var record = new NodeRecord(held.PropertyString(ModelNames.Status) ?? string.Empty, OutputsOf(held), AttemptsOf(graph, held),
                held.PropertyString(ModelNames.Error),
                Decision: Guid.TryParse(held.PropertyString(ModelNames.Decision), out var decision) ? decision : null,
                Watching: WatchOf(graph, held) is { AwaitedId: null, RangeId: null } ? null : WatchOf(graph, held));
            if (!int.TryParse(held.PropertyString(ModelNames.Index), out var index))
            {
                nodes[nodeId] = record;
                continue;
            }
            if (!items.TryGetValue(nodeId, out var ofNode))
                items[nodeId] = ofNode = new SortedList<int, NodeRecord>();
            ofNode[index] = record with { Number = new ItemNumber(index, int.TryParse(held.PropertyString(ModelNames.Total), out var total) ? total : 0) };
        }
        foreach (var (nodeId, ofNode) in items)
            if (nodes.TryGetValue(nodeId, out var aggregate))
                nodes[nodeId] = aggregate with { Items = ofNode.Values.ToList() };
        return new RunRecord(runId, pipeline.Id, JsonOf(run.PropertyString(ModelNames.Parameters)), nodes, run.CarriesFlag(ModelNames.Rehearsal),
            run.PropertyString(ModelNames.Status) ?? string.Empty, repeatedBy);
    }

    private static IReadOnlyList<AttemptRecord> AttemptsOf(PipelineGraph graph, GraphThing nodeRun)
    {
        var attempts = new List<AttemptRecord>();
        foreach (var held in graph.OutgoingTargets(nodeRun, ModelNames.Has))
        {
            if (!int.TryParse(held.PropertyString(ModelNames.Number), out var number)
                || !DateTimeOffset.TryParse(held.PropertyString(ModelNames.StartedUtc), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var started)
                || !DateTimeOffset.TryParse(held.PropertyString(ModelNames.FinishedUtc), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var finished))
                continue;
            attempts.Add(new AttemptRecord(number, started, finished, held.PropertyString(ModelNames.Status) ?? string.Empty, held.PropertyString(ModelNames.Error)));
        }
        return attempts.OrderBy(attempt => attempt.Number).ToList();
    }

    public async Task RecordAttemptAsync(Guid runId, Guid nodeId, int? index, AttemptRecord attempt, CancellationToken cancellationToken)
    {
        var nodeRunId = index is int i ? DeterministicGuid(runId, nodeId, i) : DeterministicGuid(runId, nodeId);
        var attemptId = DeterministicGuid(nodeRunId, AttemptsOfANodeRun, attempt.Number);
        var properties = new Dictionary<string, string?>
        {
            [ModelNames.Number] = attempt.Number.ToString(CultureInfo.InvariantCulture),
            [ModelNames.StartedUtc] = attempt.StartedUtc.ToString("o"),
            [ModelNames.FinishedUtc] = attempt.FinishedUtc.ToString("o"),
            [ModelNames.Status] = attempt.Status,
            [ModelNames.Error] = attempt.Error,
        };
        var attemptKind = await BoundAsync(PipelineRoles.Attempt, cancellationToken);
        await WriteThingInOneFragmentAsync(attemptId, $"Attempt {attempt.Number} {nodeRunId:N}", properties,
        [
            (attemptId, await ResolveByNameAsync(ModelNames.Is, cancellationToken), attemptKind),
            (nodeRunId, await ResolveByNameAsync(ModelNames.Has, cancellationToken), attemptId),
        ], cancellationToken);
        await SetPropertyAsync(nodeRunId, ModelNames.Attempts, attempt.Number.ToString(CultureInfo.InvariantCulture), cancellationToken);
    }

    // Attempts are numbered under their node run, so their identifiers are derived in a space of their own.
    private static readonly Guid AttemptsOfANodeRun = new("a77e3b1c-6d0e-4c4f-9b2a-5f1e2d3c4b5a");

    public async Task<IReadOnlyList<UnfinishedNodeRun>> UnfinishedNodeRunsAsync(CancellationToken cancellationToken)
    {
        var graph = await HeldRunsAsync(cancellationToken);
        var unfinished = new List<UnfinishedNodeRun>();
        foreach (var nodeRun in graph.Things)
        {
            if (nodeRun.PropertyString(ModelNames.Status) is not (RunStatus.Trying or RunStatus.Running)
                || !Guid.TryParse(nodeRun.PropertyString(ModelNames.NodeId), out var nodeId)
                || graph.RelationshipsInto(nodeRun, ModelNames.Has).FirstOrDefault() is not { } held)
                continue;
            unfinished.Add(new UnfinishedNodeRun(held.SubjectId, nodeId));
        }
        return unfinished;
    }

    private static IReadOnlyDictionary<string, JsonElement> OutputsOf(GraphThing nodeRun) =>
        JsonOf(nodeRun.PropertyString(ModelNames.Outputs)) is { ValueKind: JsonValueKind.Object } outputs
            ? outputs.EnumerateObject().ToDictionary(output => output.Name, output => output.Value.Clone(), StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    private static JsonElement JsonOf(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return default;
        try { return JsonDocument.Parse(text).RootElement.Clone(); }
        catch (JsonException) { return default; }
    }

    private static async Task<string> ErrorOfAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(body);
            if (TryGetPropertyCaseInsensitive(document.RootElement, "error", out var error) && error.ValueKind == JsonValueKind.String)
                return error.GetString()!;
        }
        catch (JsonException)
        {
        }
        return $"HTTP {(int)response.StatusCode}";
    }

    public async Task<IReadOnlyList<HeldWait>> WaitingNodeRunsAsync(CancellationToken cancellationToken)
    {
        var graph = await HeldRunsAsync(cancellationToken);
        var waits = new List<HeldWait>();
        foreach (var nodeRun in graph.Things)
        {
            if (nodeRun.PropertyString(ModelNames.Status) != RunStatus.Waiting
                || !Guid.TryParse(nodeRun.PropertyString(ModelNames.NodeId), out var nodeId)
                || graph.RelationshipsInto(nodeRun, ModelNames.Has).FirstOrDefault() is not { } held)
                continue;
            waits.Add(new HeldWait(held.SubjectId, nodeId, WatchOf(graph, nodeRun), AnswerOf(graph, nodeRun)));
        }
        return waits;
    }

    private static Watch WatchOf(PipelineGraph graph, GraphThing nodeRun) =>
        new(graph.RelationshipsAlong(nodeRun, PipelineRoles.AwaitedThing).Select(rel => (Guid?)rel.TargetId).FirstOrDefault(),
            graph.RelationshipsAlong(nodeRun, PipelineRoles.StateWatch).Select(rel => (Guid?)rel.TargetId).FirstOrDefault());

    // The platform marks its record of calling the node run's connection Done once this orchestrator answered the call.
    private static WaitAnswer AnswerOf(PipelineGraph graph, GraphThing nodeRun) =>
        graph.RelationshipsIntoAlong(nodeRun, PipelineRoles.CallRecord).FirstOrDefault() is not { } record ? WaitAnswer.None
        : record.Properties.TryGetValue(CallStateProperty, out var state) && state.ValueKind == JsonValueKind.String && state.GetString() == CallDone ? WaitAnswer.Done
        : WaitAnswer.Underway;

    private const string CallStateProperty = "__CallState";
    private const string CallDone = "Done";

    private async Task<PipelineGraph> HeldRunsAsync(CancellationToken cancellationToken)
    {
        var selector = new
        {
            ids = new[] { PipelineRoles.Orchestrator.Id, PipelineRoles.AwaitedThing.Id, PipelineRoles.StateWatch.Id, PipelineRoles.CallRecord.Id },
            names = new[] { ModelNames.Binds, ModelNames.Has },
            traverse = new object[]
            {
                new { predicate = ModelNames.Binds, direction = "outgoing", depth = 1 },
                new { predicate = ModelNames.Has, direction = "outgoing", depth = 2 },
            },
            includeIsAncestors = false,
            includeRelationships = true,
            relationships = new object[]
            {
                new { predicate = ModelNames.Binds, direction = "outgoing" },
                new { predicate = ModelNames.Has, direction = "outgoing" },
                new { predicateRole = PipelineRoles.AwaitedThing.Id, direction = "outgoing" },
                new { predicateRole = PipelineRoles.StateWatch.Id, direction = "outgoing" },
                new { predicateRole = PipelineRoles.CallRecord.Id, direction = "incoming" },
            },
        };
        return await LoadSnapshotAsync(selector, TimeSpan.FromSeconds(60), cancellationToken);
    }

    public async Task RelateProducedAsync(Guid runId, Guid nodeId, int? index, IReadOnlyList<Guid> producedThingIds, CancellationToken cancellationToken)
    {
        if (producedThingIds.Count == 0) return;
        var predicateId = await TryBoundAsync(PipelineRoles.ProducedThing, cancellationToken);
        if (predicateId is null)
        {
            Logger.LogWarning("The model binds nothing to the role {Role}; run {RunId} records what node {NodeId} produced without relating to it",
                PipelineRoles.ProducedThing.Word, runId, nodeId);
            return;
        }
        var nodeRunId = index is int i ? DeterministicGuid(runId, nodeId, i) : DeterministicGuid(runId, nodeId);
        foreach (var producedId in producedThingIds)
            await RelateAsync(nodeRunId, predicateId.Value, producedId, cancellationToken);
    }

    public Task SetRunResultAsync(Guid runId, JsonElement result, CancellationToken cancellationToken) =>
        SetPropertyAsync(runId, ModelNames.Result, result.GetRawText(), cancellationToken);

    public async Task<bool> IsStopAsync(Guid relationshipId, CancellationToken cancellationToken) =>
        await PredicateOfAsync(relationshipId, cancellationToken) is { } predicateId
        && await TryBoundAsync(PipelineRoles.RunStopping, cancellationToken) == predicateId;

    // Related to the Thing last: that is the write the broker sends back as a stop, and by then the request already
    // names the run that wrote it, so that run is never stopped by it.
    public async Task WriteStopRequestAsync(Guid runId, Guid thingId, string? reason, CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();
        await WriteThingInOneFragmentAsync(requestId, $"StopRequest {requestId:N}", new Dictionary<string, string?> { [ModelNames.Reason] = reason },
        [
            (requestId, await ResolveByNameAsync(ModelNames.Is, cancellationToken), await BoundAsync(PipelineRoles.StopRequest, cancellationToken)),
            (requestId, await BoundAsync(PipelineRoles.RequestedBy, cancellationToken), runId),
        ], cancellationToken);
        await RelateAsync(requestId, await BoundAsync(PipelineRoles.RunStopping, cancellationToken), thingId, cancellationToken);
    }

    public async Task<StopRequested> StopRequestedAsync(Guid stopRequestId, Guid targetId, CancellationToken cancellationToken)
    {
        var selector = new
        {
            ids = new[] { stopRequestId, targetId, PipelineRoles.RunSubject.Id, PipelineRoles.PipelineRun.Id, PipelineRoles.RequestedBy.Id },
            names = new[] { ModelNames.Binds, ModelNames.Is },
            traverse = new object[]
            {
                new { predicate = ModelNames.Binds, direction = "outgoing", depth = 1 },
                new { predicateRole = PipelineRoles.RunSubject.Id, direction = "incoming", depth = 1 },
            },
            includeIsAncestors = false,
            includeRelationships = true,
            relationships = new object[]
            {
                new { predicate = ModelNames.Binds, direction = "outgoing" },
                new { predicate = ModelNames.Is, direction = "outgoing" },
                new { predicateRole = PipelineRoles.RunSubject.Id, direction = "incoming" },
                new { predicateRole = PipelineRoles.RequestedBy.Id, direction = "outgoing" },
            },
        };
        var graph = await LoadSnapshotAsync(selector, TimeSpan.FromSeconds(15), cancellationToken);
        var request = graph.Thing(stopRequestId);
        var reason = request?.PropertyString(ModelNames.Reason) is { Length: > 0 } stated ? stated : "Stopped by request.";
        var requestedBy = request is null ? [] : graph.RelationshipsAlong(request, PipelineRoles.RequestedBy).Select(along => along.TargetId).ToHashSet();
        if (graph.Thing(targetId) is not { } target) return new StopRequested(stopRequestId, reason, []);
        if (graph.IsOf(target, PipelineRoles.PipelineRun))
            return new StopRequested(stopRequestId, reason, requestedBy.Contains(target.Id) ? [] : [target.Id]);

        var going = graph.IncomingAlong(target, PipelineRoles.RunSubject)
            .Where(run => !requestedBy.Contains(run.Id) && run.PropertyString(ModelNames.Status) is RunStatus.Running or RunStatus.Waiting or RunStatus.Trying)
            .Select(run => run.Id)
            .ToList();
        return new StopRequested(stopRequestId, reason, going);
    }

    public Task RelateStoppedByAsync(Guid runId, Guid stopRequestId, CancellationToken cancellationToken) =>
        RelateIfBoundAsync(runId, PipelineRoles.StoppedBy, stopRequestId, cancellationToken);

    public async Task<RunSubject?> SubjectAsync(Guid thingId, CancellationToken cancellationToken)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10));
        var response = await client.GetAsync($"{MyceliumUrl}/api/things/{thingId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        var thing = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return new RunSubject(thingId,
            TryGetPropertyCaseInsensitive(thing, "Name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString()! : string.Empty);
    }

    public async Task<Guid?> PredicateOfAsync(Guid relationshipId, CancellationToken cancellationToken)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10));
        var response = await client.GetAsync($"{MyceliumUrl}/api/relationships/{relationshipId}", cancellationToken);
        if (!response.IsSuccessStatusCode) return null;

        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return TryGetPropertyCaseInsensitive(root, "PredicateId", out var predicate)
            && predicate.ValueKind == JsonValueKind.String && Guid.TryParse(predicate.GetString(), out var predicateId)
                ? predicateId
                : null;
    }

    // Related to the system last: that write is the one the broker sends, and the sender it reaches reads
    // a message already whole and already held by its run.
    public async Task SendMessageAsync(Guid runId, Telling telling, JsonElement payload, CancellationToken cancellationToken)
    {
        var messageId = Guid.NewGuid();
        var messageKind = await BoundAsync(PipelineRoles.SentMessage, cancellationToken);
        await WriteThingInOneFragmentAsync(messageId, $"Message to {telling.SystemName} {runId:N}",
            new Dictionary<string, string?> { [ModelNames.Payload] = payload.GetRawText() },
        [
            (messageId, await ResolveByNameAsync(ModelNames.Is, cancellationToken), messageKind),
            (runId, await ResolveByNameAsync(ModelNames.Has, cancellationToken), messageId),
        ], cancellationToken);
        await RelateAsync(messageId, telling.ConnectionId, telling.SystemId, cancellationToken);
    }

    public async Task<NodeCallResult> CallAsync(string subdomain, JsonElement envelope, CancellationToken cancellationToken)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromMinutes(5));
        var response = await client.PostAsync($"{MyceliumUrl}/api/endpoints/{Uri.EscapeDataString(subdomain)}",
            new StringContent(envelope.GetRawText(), Encoding.UTF8, "application/json"), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new NodeCallResult((int)response.StatusCode, body);
    }

    private async Task<PipelineGraph> LoadSnapshotAsync(object selector, TimeSpan timeout, CancellationToken cancellationToken) =>
        SnapshotParser.Parse(await SnapshotAsync(selector, timeout, cancellationToken));

    private async Task<JsonElement> SnapshotAsync(object selector, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var client = await CreateAuthenticatedClientAsync(timeout);
        var response = await client.PostAsync($"{MyceliumUrl}/api/snapshots",
            new StringContent(JsonSerializer.Serialize(selector), Encoding.UTF8, "application/json"), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    public Task<IReadOnlyList<Cadence>> CadencesAsync(CancellationToken cancellationToken)
    {
        lock (_followLock) _followingCadences ??= FollowCadencesAsync(_stopping.Token);
        return _cadences.CurrentAsync(cancellationToken);
    }

    // A derived value is published live-only and never replayed, so a dropped stream is not resumed: the gateway
    // subscribes again and takes the fresh snapshot.
    private async Task FollowCadencesAsync(CancellationToken stopping)
    {
        var failuresInARow = 0;
        while (!stopping.IsCancellationRequested)
        {
            Guid? subscriptionId = null;
            try
            {
                var opened = await SubscribeToCadencesAsync(stopping);
                subscriptionId = opened.SubscriptionId;
                _cadences.Replace(opened.Snapshot.Things.Where(thing => !thing.IsArchetype).Select(HeldCadences.CadenceOf));
                if (failuresInARow > 0) Logger.LogInformation("Following the cadences works again");
                failuresInARow = 0;
                await foreach (var change in StreamAsync(opened.SubscriptionId, opened.Watermark, stopping))
                    _cadences.Apply(change);
                Logger.LogInformation("The cadence stream ended; subscribing again");
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
            catch (Exception failure)
            {
                _cadences.NoteFailure(failure);
                if (failuresInARow++ == 0) Logger.LogWarning(failure, "Following the cadences failed; subscribing again less often until it works");
            }
            if (subscriptionId is { } left) await UnsubscribeQuietlyAsync(left);
            if (stopping.IsCancellationRequested) return;
            try { await Task.Delay(failuresInARow == 0 ? CadenceStreamRetry : CadenceKeeper.WaitBeforeLook(failuresInARow), stopping); }
            catch (OperationCanceledException) { return; }
        }
    }

    // A kind of cadence is not one, so the archetype is left out of what is held.
    private async Task<SubscribeResult> SubscribeToCadencesAsync(CancellationToken cancellationToken)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(30));
        var response = await client.PostAsync($"{MyceliumUrl}/api/subscriptions", JsonContent.Create(new
        {
            boundTypes = new[] { PipelineRoles.Cadence.Id },
            includeIsAncestors = false,
            includeRelationships = false,
            includeLaterMatches = true,
            includeSnapshot = true,
        }), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SubscribeResult>(SnapshotJson, cancellationToken)
            ?? throw new InvalidOperationException("The platform opened a subscription and answered nothing.");
    }

    private async IAsyncEnumerable<ModelChangeEvent> StreamAsync(Guid subscriptionId, long watermark, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var client = await CreateAuthenticatedClientAsync(Timeout.InfiniteTimeSpan);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{MyceliumUrl}/api/subscriptions/{subscriptionId}/stream?lastEventId={watermark}");
        request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await foreach (var frame in SseEventReader.ReadAsync(stream, cancellationToken))
            if (ChangeOf(frame) is { } change) yield return change;
    }

    // The kind is read off the frame's event line: the data names it too, but as a number.
    private static ModelChangeEvent? ChangeOf(SseFrame frame)
    {
        if (string.IsNullOrEmpty(frame.EventType) || string.IsNullOrEmpty(frame.Data)) return null;
        try
        {
            using var document = JsonDocument.Parse(frame.Data);
            var data = document.RootElement;
            return new ModelChangeEvent
            {
                Kind = frame.EventType,
                EntityId = TryGetPropertyCaseInsensitive(data, "EntityId", out var entity) && entity.TryGetGuid(out var entityId) ? entityId : Guid.Empty,
                PropertyName = TryGetPropertyCaseInsensitive(data, "PropertyName", out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null,
                Value = TryGetPropertyCaseInsensitive(data, "Value", out var value) ? value.Clone() : null,
                Thing = TryGetPropertyCaseInsensitive(data, "Thing", out var thing) && thing.ValueKind == JsonValueKind.Object
                    ? thing.Deserialize<SnapshotThing>(SnapshotJson)
                    : null,
            };
        }
        catch (JsonException) { return null; }
    }

    private async Task UnsubscribeQuietlyAsync(Guid subscriptionId)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(5));
            await client.DeleteAsync($"{MyceliumUrl}/api/subscriptions/{subscriptionId}");
        }
        catch (Exception failure)
        {
            Logger.LogDebug(failure, "Subscription {SubscriptionId} to the cadences could not be deleted", subscriptionId);
        }
    }

    // Registered under its own type and under the interface, so the container disposes the one instance twice.
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        await _stopping.CancelAsync();
        if (_followingCadences is { } following)
        {
            try { await following.WaitAsync(DisposeWait); }
            catch (Exception) { }
        }
        _stopping.Dispose();
    }

    private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web);

    // Applied to the held cadence as it is written: the next look comes before the stream need have carried it,
    // and a held cadence read with the old instant would start the same run again.
    public async Task SetLastDueAsync(Guid cadenceId, DateTimeOffset due, CancellationToken cancellationToken)
    {
        await SetPropertyAsync(cadenceId, ModelNames.LastDue, due.ToString("o"), cancellationToken, "vos.DateTime");
        _cadences.MarkDue(cadenceId, due);
    }

    // The Thing playing a role, found through the role Thing rather than by a name this orchestrator was told.
    // Asked for on its own: boundTypes would answer with every Thing that already `is` it, which for a run
    // archetype is every run ever recorded. This runs once per node run, so the answer is kept for the process.
    private async Task<Guid> BoundAsync(Role role, CancellationToken cancellationToken) =>
        await TryBoundAsync(role, cancellationToken)
        ?? throw new InvalidOperationException($"The model binds nothing to the role {role.Word}.");

    private async Task<Guid?> TryBoundAsync(Role role, CancellationToken cancellationToken)
    {
        if (_boundByRole.TryGetValue(role.Id, out var cached)) return cached;

        var selector = new
        {
            ids = new[] { role.Id },
            names = new[] { ModelNames.Binds },
            traverse = new object[] { new { predicate = ModelNames.Binds, direction = "outgoing", depth = 1 } },
            includeIsAncestors = false,
            includeRelationships = true,
            relationships = new object[] { new { predicate = ModelNames.Binds, direction = "outgoing" } },
        };
        var graph = await LoadSnapshotAsync(selector, TimeSpan.FromSeconds(15), cancellationToken);

        if (graph.Bound(role) is not { } bound) return null;
        _boundByRole[role.Id] = bound.Id;
        return bound.Id;
    }

    // Written apart, the `is` of a Thing already holding a value its kind declares is refused; in one fragment the broker stores that value as the kind's override.
    // A fragment naming a Thing that already exists changes its values and leaves its name alone.
    private Task WriteThingInOneFragmentAsync(
        Guid id, string name, IReadOnlyDictionary<string, string?> properties,
        IReadOnlyList<(Guid Subject, Guid Predicate, Guid Target)> relationships, CancellationToken cancellationToken) =>
        WriteFragmentAsync(name, [(id, name, properties)], relationships, [], cancellationToken);

    // A relationship retracted here goes in the same commit as the writes; one already gone is passed over.
    private async Task WriteFragmentAsync(
        string name, IReadOnlyList<(Guid Id, string Name, IReadOnlyDictionary<string, string?> Properties)> things,
        IReadOnlyList<(Guid Subject, Guid Predicate, Guid Target)> relationships,
        IReadOnlyList<(Guid Subject, Guid Predicate, Guid Target)> retractions, CancellationToken cancellationToken)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(15));
        var response = await client.PostAsync($"{MyceliumUrl}/api/model/fragment", JsonContent.Create(new
        {
            name,
            things = things.Select(thing => new { id = thing.Id, name = thing.Name, properties = Envelopes(thing.Properties) }),
            relationships = relationships.Select(edge => new
            {
                id = DeterministicGuid(DeterministicGuid(edge.Subject, edge.Predicate), edge.Target),
                subject = edge.Subject,
                predicate = edge.Predicate,
                target = edge.Target,
            }),
            retractions = retractions.Count == 0 ? null : retractions.Select(edge => new { subject = edge.Subject, predicate = edge.Predicate, target = edge.Target }),
        }), cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"The platform refused '{name}': {await ErrorOfAsync(response, cancellationToken)}");
    }

    // The broker refuses a property written as a bare value, with a 400 carrying no body to say why.
    private static Dictionary<string, object> Envelopes(IReadOnlyDictionary<string, string?> properties) =>
        properties.ToDictionary(
            property => property.Key,
            property => (object)new { typeInfo = PropertyType, value = property.Value });

    private async Task SetPropertyAsync(Guid thingId, string name, string value, CancellationToken cancellationToken, string type = PropertyType)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(15));
        var response = await client.PutAsync($"{MyceliumUrl}/api/things/{thingId}/properties",
            JsonContent.Create(new { name, type, value }), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    // A stable id per (run, node) so a node's NodeRun is one Thing across its running→terminal
    // transitions — required for the SSE animation to see property changes, not duplicate creates.
    private static Guid DeterministicGuid(Guid runId, Guid nodeId)
    {
        Span<byte> buffer = stackalloc byte[32];
        runId.TryWriteBytes(buffer[..16]);
        nodeId.TryWriteBytes(buffer[16..]);
        return new Guid(System.Security.Cryptography.MD5.HashData(buffer));
    }

    // Per-item NodeRun id for a fan-out — stable per (run, node, item index).
    private static Guid DeterministicGuid(Guid runId, Guid nodeId, int index)
    {
        Span<byte> buffer = stackalloc byte[36];
        runId.TryWriteBytes(buffer[..16]);
        nodeId.TryWriteBytes(buffer[16..32]);
        BitConverter.TryWriteBytes(buffer[32..], index);
        return new Guid(System.Security.Cryptography.MD5.HashData(buffer));
    }

    public Task WriteRelationshipAsync(Guid subjectId, Guid predicateId, Guid targetId, CancellationToken cancellationToken) =>
        RelateAsync(subjectId, predicateId, targetId, cancellationToken);

    private async Task RelateAsync(Guid subjectId, Guid predicateId, Guid targetId, CancellationToken cancellationToken)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(15));
        var response = await client.PostAsync($"{MyceliumUrl}/api/relationships",
            JsonContent.Create(new { subjectId, predicateId, targetId }), cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"The platform refused a relationship: {await ErrorOfAsync(response, cancellationToken)}",
                null, response.StatusCode);
    }

    private async Task<Guid> ResolveByNameAsync(string name, CancellationToken cancellationToken)
    {
        if (_thingIdByName.TryGetValue(name, out var cached)) return cached;

        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(15));
        var response = await client.GetAsync($"{MyceliumUrl}/api/things?name={Uri.EscapeDataString(name)}", cancellationToken);
        response.EnsureSuccessStatusCode();
        var thing = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        if (TryGetPropertyCaseInsensitive(thing, "Id", out var idEl)
            && idEl.ValueKind == JsonValueKind.String && Guid.TryParse(idEl.GetString(), out var id))
        {
            _thingIdByName[name] = id;
            return id;
        }
        throw new InvalidOperationException($"Could not resolve a Thing named '{name}'.");
    }
}
