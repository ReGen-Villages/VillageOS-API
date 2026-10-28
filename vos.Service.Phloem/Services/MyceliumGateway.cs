using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
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
public sealed class MyceliumGateway : MyceliumClientBase, IMyceliumGateway
{
    // Everything this gateway records about a run is text, so one type name serves every property it writes.
    private const string PropertyType = "vos.String";

    private readonly ConcurrentDictionary<string, Guid> _thingIdByName = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, Guid> _boundByRole = new();
    private readonly ConcurrentDictionary<Guid, byte> _createdNodeRuns = new();

    public MyceliumGateway(IHttpClientFactory httpClientFactory, ILogger<MyceliumGateway> logger, string myceliumUrl, string? serviceToken = null, string? apiKey = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken, apiKey: apiKey) { }

    public Task<bool> RegisterAsync(int port) => RegisterAsync(port, "Phloem", "endpoint-service");

    // A role is read off the drawing only when its role Thing is in it.
    private static readonly IReadOnlyList<Role> DrawingRoles =
        [.. PipelineRoles.DagRoles, PipelineRoles.ExternalSystem, PipelineRoles.StandsFor, PipelineRoles.ToldThrough, PipelineRoles.Choice,
         PipelineRoles.CompletionRange, PipelineRoles.AwaitsThrough, PipelineRoles.HandsOverTo];

    private static readonly IReadOnlyList<Role> StartRoles =
    [
        PipelineRoles.Pipeline, PipelineRoles.PipelineInput, PipelineRoles.Connection,
        PipelineRoles.StateWatch, PipelineRoles.StandsFor, PipelineRoles.PipelineStart,
        PipelineRoles.MessageKind, PipelineRoles.ExternalSystem,
    ];

    public async Task<PipelineGraph> LoadPipelineSubgraphAsync(Guid pipelineId, CancellationToken cancellationToken)
    {
        // Pull the whole structural closure in one snapshot. Three things the traversal will not reach on
        // its own are asked for outright: the built-in predicate Things, because includeRelationships
        // does not pull predicate Things; every Port and wire Thing, because includeIsAncestors does not
        // pull a prototype's has-children; and the role Things with what each binds, so that a role missing
        // from the snapshot means the model binds it to nothing rather than that this pipeline plays that role
        // nowhere. See SelectorResolver. After the nodes come the system an end stands for, the connection
        // that system is told through, and the state a waiting node waits for, along the predicates those roles bind.
        // Only outgoing relationships travel: every edge the drawing is read along runs outward from a Thing in the
        // answer, and without the rule the broker also returns every `is` written to an ancestor such as Connection.
        var selector = new
        {
            ids = DrawingRoles.Select(role => role.Id).Prepend(pipelineId).ToArray(),
            names = new[] { ModelNames.Is, ModelNames.Has, ModelNames.Binds },
            boundTypes = new[] { PipelineRoles.Port.Id, PipelineRoles.PipelineWire.Id },
            traverse = new object[]
            {
                new { predicate = ModelNames.Has, direction = "outgoing", depth = 8 },
                new { predicate = ModelNames.Binds, direction = "outgoing", depth = 1 },
                new { predicateRole = PipelineRoles.StandsFor.Id, direction = "outgoing", depth = 1 },
                new { predicateRole = PipelineRoles.ToldThrough.Id, direction = "outgoing", depth = 1 },
                new { predicateRole = PipelineRoles.Choice.Id, direction = "outgoing", depth = 1 },
                new { predicateRole = PipelineRoles.CompletionRange.Id, direction = "outgoing", depth = 1 },
                new { predicateRole = PipelineRoles.HandsOverTo.Id, direction = "outgoing", depth = 1 },
                new { predicateRole = PipelineRoles.ComparesWith.Id, direction = "outgoing", depth = 1 },
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

    public async Task CreateRunAsync(
        Guid runId, Guid pipelineId, CancellationToken cancellationToken, RunSubject? subject = null, Guid? requestId = null,
        JsonElement? parameters = null, Guid? startedBy = null, string? refusal = null, bool rehearsal = false)
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
        await CreateThingWithIdAsync(runId, $"PipelineRun {runId:N}", properties, cancellationToken);

        await RelateAsync(runId, ModelNames.Is, await BoundAsync(PipelineRoles.PipelineRun, cancellationToken), cancellationToken);
        await RelateAsync(runId, ModelNames.Of, pipelineId, cancellationToken);

        if (startedBy is { } starter) await RelateIfBoundAsync(runId, PipelineRoles.StartingRun, starter, cancellationToken);
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
        IReadOnlyDictionary<string, JsonElement>? outputs = null, Guid? decision = null)
    {
        var nodeRunId = index is int i ? DeterministicGuid(runId, nodeId, i) : DeterministicGuid(runId, nodeId);
        var terminal = RunStatus.IsFinished(status);
        var now = DateTime.UtcNow.ToString("o");
        if (_createdNodeRuns.TryAdd(nodeRunId, 0))
        {
            // First status for this NodeRun — create the Thing and wire it into the run, declaring every
            // property a later transition writes.
            var properties = new Dictionary<string, string?>
            {
                [ModelNames.Status] = status,
                [ModelNames.NodeId] = nodeId.ToString(),
                [ModelNames.Error] = error,
                [ModelNames.StartedUtc] = now,
                [ModelNames.FinishedUtc] = terminal ? now : null,
                [ModelNames.Outputs] = outputs is null ? null : OutputsText(outputs),
                [ModelNames.Attempts] = null,
                [ModelNames.Decision] = decision?.ToString(),
            };
            if (index is int idx)
            {
                properties[ModelNames.Index] = idx.ToString();
                properties[ModelNames.Total] = total.ToString();
            }
            await CreateThingWithIdAsync(nodeRunId, index is int x ? $"NodeRun {nodeName} #{x}" : $"NodeRun {nodeName}", properties, cancellationToken);
            await RelateAsync(nodeRunId, ModelNames.Is, await BoundAsync(PipelineRoles.NodeRun, cancellationToken), cancellationToken);
            await RelateAsync(runId, ModelNames.Has, nodeRunId, cancellationToken);
            return;
        }

        // Subsequent transition (running -> terminal) on the same Thing — a property change the SSE view sees.
        await SetPropertyAsync(nodeRunId, ModelNames.Status, status, cancellationToken);
        if (error != null) await SetPropertyAsync(nodeRunId, ModelNames.Error, error, cancellationToken);
        if (terminal) await SetPropertyAsync(nodeRunId, ModelNames.FinishedUtc, now, cancellationToken);
        if (outputs is not null) await SetPropertyAsync(nodeRunId, ModelNames.Outputs, OutputsText(outputs), cancellationToken);
        if (decision is { } decided) await SetPropertyAsync(nodeRunId, ModelNames.Decision, decided.ToString(), cancellationToken);
    }

    private static string OutputsText(IReadOnlyDictionary<string, JsonElement> outputs) =>
        JsonSerializer.Serialize(outputs);

    public async Task SetRunStatusAsync(Guid runId, string status, CancellationToken cancellationToken, string? error = null)
    {
        await SetPropertyAsync(runId, ModelNames.Status, status, cancellationToken);
        if (error != null) await SetPropertyAsync(runId, ModelNames.Error, error, cancellationToken);
        if (RunStatus.IsFinished(status)) await SetPropertyAsync(runId, ModelNames.FinishedUtc, DateTime.UtcNow.ToString("o"), cancellationToken);
    }

    public async Task<RunRecord?> ReadRunRecordAsync(Guid runId, CancellationToken cancellationToken)
    {
        var selector = new
        {
            ids = new[] { runId },
            names = new[] { ModelNames.Has, ModelNames.Of },
            traverse = new object[]
            {
                new { predicate = ModelNames.Has, direction = "outgoing", depth = 2 },
                new { predicate = ModelNames.Of, direction = "outgoing", depth = 1 },
            },
            includeIsAncestors = false,
            includeRelationships = true,
            relationships = new object[]
            {
                new { predicate = ModelNames.Has, direction = "outgoing" },
                new { predicate = ModelNames.Of, direction = "outgoing" },
            },
        };
        var graph = await LoadSnapshotAsync(selector, TimeSpan.FromSeconds(15), cancellationToken);
        if (graph.Thing(runId) is not { } run || graph.OutgoingTargets(run, ModelNames.Of).FirstOrDefault() is not { } pipeline)
            return null;

        var nodes = new Dictionary<Guid, NodeRecord>();
        var items = new Dictionary<Guid, SortedList<int, NodeRecord>>();
        foreach (var held in graph.OutgoingTargets(run, ModelNames.Has))
        {
            if (!Guid.TryParse(held.PropertyString(ModelNames.NodeId), out var nodeId)) continue;
            _createdNodeRuns.TryAdd(held.Id, 0);
            var record = new NodeRecord(held.PropertyString(ModelNames.Status) ?? string.Empty, OutputsOf(held), AttemptsOf(graph, held),
                held.PropertyString(ModelNames.Error),
                Decision: Guid.TryParse(held.PropertyString(ModelNames.Decision), out var decision) ? decision : null);
            if (!int.TryParse(held.PropertyString(ModelNames.Index), out var index))
            {
                nodes[nodeId] = record;
                continue;
            }
            if (!items.TryGetValue(nodeId, out var ofNode))
                items[nodeId] = ofNode = new SortedList<int, NodeRecord>();
            ofNode[index] = record;
        }
        foreach (var (nodeId, ofNode) in items)
            if (nodes.TryGetValue(nodeId, out var aggregate))
                nodes[nodeId] = aggregate with { Items = ofNode.Values.ToList() };
        return new RunRecord(runId, pipeline.Id, JsonOf(run.PropertyString(ModelNames.Parameters)), nodes, run.CarriesFlag(ModelNames.Rehearsal),
            run.PropertyString(ModelNames.Status) ?? string.Empty);
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
        await CreateThingWithIdAsync(attemptId, $"Attempt {attempt.Number} {nodeRunId:N}", properties, cancellationToken);
        await RelateAsync(attemptId, ModelNames.Is, await BoundAsync(PipelineRoles.Attempt, cancellationToken), cancellationToken);
        await RelateAsync(nodeRunId, ModelNames.Has, attemptId, cancellationToken);
        await SetPropertyAsync(nodeRunId, ModelNames.Attempts, attempt.Number.ToString(CultureInfo.InvariantCulture), cancellationToken);
    }

    // Attempts are numbered under their node run, so their identifiers are derived in a space of their own.
    private static readonly Guid AttemptsOfANodeRun = new("a77e3b1c-6d0e-4c4f-9b2a-5f1e2d3c4b5a");

    public async Task<IReadOnlyList<TryingNodeRun>> TryingNodeRunsAsync(CancellationToken cancellationToken)
    {
        var graph = await NodeRunsAsync(cancellationToken);
        var trying = new List<TryingNodeRun>();
        foreach (var nodeRun in graph.Things)
        {
            if (nodeRun.PropertyString(ModelNames.Status) != RunStatus.Trying
                || !Guid.TryParse(nodeRun.PropertyString(ModelNames.NodeId), out var nodeId)
                || graph.RelationshipsInto(nodeRun, ModelNames.Has).FirstOrDefault() is not { } held)
                continue;
            trying.Add(new TryingNodeRun(held.SubjectId, nodeId, !string.IsNullOrEmpty(nodeRun.PropertyString(ModelNames.Index))));
        }
        return trying;
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

    public async Task RelateAwaitedAsync(Guid runId, Guid nodeId, Guid awaitedId, CancellationToken cancellationToken) =>
        await RelateAsync(DeterministicGuid(runId, nodeId), await BoundAsync(PipelineRoles.AwaitedThing, cancellationToken), awaitedId, cancellationToken);

    public Task ForgetAwaitedAsync(Guid runId, Guid nodeId, CancellationToken cancellationToken) =>
        ForgetAlongAsync(DeterministicGuid(runId, nodeId), PipelineRoles.AwaitedThing, cancellationToken);

    public async Task<VigilPlacement> PlaceVigilAsync(Guid subjectId, Guid rangeId, CancellationToken cancellationToken)
    {
        var connectionId = await BoundAsync(PipelineRoles.RunResumption, cancellationToken);
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(15));
        var response = await client.PostAsync($"{MyceliumUrl}/api/vigils",
            JsonContent.Create(new { subjectId, rangeId, connectionId }), cancellationToken);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
            return new VigilPlacement.Refused(await ErrorOfAsync(response, cancellationToken));
        response.EnsureSuccessStatusCode();

        var vigil = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return TryGetPropertyCaseInsensitive(vigil, "Id", out var id) && id.ValueKind == JsonValueKind.String && Guid.TryParse(id.GetString(), out var vigilId)
            ? new VigilPlacement.Placed(vigilId)
            : throw new InvalidOperationException("The platform placed a vigil but named no identifier for it.");
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

    public async Task RelateHeldVigilAsync(Guid runId, Guid nodeId, Guid vigilId, CancellationToken cancellationToken)
    {
        var nodeRunId = DeterministicGuid(runId, nodeId);
        await ForgetAlongAsync(nodeRunId, PipelineRoles.HeldVigil, cancellationToken);
        await RelateAsync(nodeRunId, await BoundAsync(PipelineRoles.HeldVigil, cancellationToken), vigilId, cancellationToken);
    }

    public async Task<VigilHolder?> HolderOfVigilAsync(Guid vigilId, CancellationToken cancellationToken)
    {
        var selector = new
        {
            ids = new[] { vigilId, PipelineRoles.HeldVigil.Id },
            names = new[] { ModelNames.Binds, ModelNames.Has },
            traverse = new object[]
            {
                new { predicate = ModelNames.Binds, direction = "outgoing", depth = 1 },
                new { predicateRole = PipelineRoles.HeldVigil.Id, direction = "incoming", depth = 1 },
                new { predicate = ModelNames.Has, direction = "incoming", depth = 1 },
            },
            includeIsAncestors = false,
            includeRelationships = true,
            relationships = new object[]
            {
                new { predicate = ModelNames.Binds, direction = "outgoing" },
                new { predicateRole = PipelineRoles.HeldVigil.Id, direction = "incoming" },
                new { predicate = ModelNames.Has, direction = "incoming" },
            },
        };
        var graph = await LoadSnapshotAsync(selector, TimeSpan.FromSeconds(15), cancellationToken);
        if (graph.Thing(vigilId) is not { } vigil
            || graph.IncomingAlong(vigil, PipelineRoles.HeldVigil).FirstOrDefault() is not { } nodeRun
            || !Guid.TryParse(nodeRun.PropertyString(ModelNames.NodeId), out var nodeId)
            || graph.RelationshipsInto(nodeRun, ModelNames.Has).FirstOrDefault() is not { } held)
            return null;
        return new VigilHolder(held.SubjectId, nodeId);
    }

    public async Task<IReadOnlyList<HeldWait>> WaitingNodeRunsAsync(CancellationToken cancellationToken)
    {
        var graph = await NodeRunsAsync(cancellationToken);
        var waits = new List<HeldWait>();
        foreach (var nodeRun in graph.Things)
        {
            if (nodeRun.PropertyString(ModelNames.Status) != RunStatus.Waiting
                || !Guid.TryParse(nodeRun.PropertyString(ModelNames.NodeId), out var nodeId)
                || graph.RelationshipsInto(nodeRun, ModelNames.Has).FirstOrDefault() is not { } held)
                continue;
            waits.Add(new HeldWait(held.SubjectId, nodeId,
                graph.RelationshipsAlong(nodeRun, PipelineRoles.AwaitedThing).Select(rel => (Guid?)rel.TargetId).FirstOrDefault(),
                graph.RelationshipsAlong(nodeRun, PipelineRoles.HeldVigil).Select(rel => (Guid?)rel.TargetId).FirstOrDefault()));
        }
        return waits;
    }

    // Every node run the model holds is read, which is a startup cost that grows with the runs ever recorded.
    private async Task<PipelineGraph> NodeRunsAsync(CancellationToken cancellationToken)
    {
        var selector = new
        {
            ids = new[] { PipelineRoles.AwaitedThing.Id, PipelineRoles.HeldVigil.Id },
            names = new[] { ModelNames.Binds, ModelNames.Has },
            boundTypes = new[] { PipelineRoles.NodeRun.Id },
            traverse = new object[] { new { predicate = ModelNames.Binds, direction = "outgoing", depth = 1 } },
            includeIsAncestors = false,
            includeRelationships = true,
            relationships = new object[]
            {
                new { predicate = ModelNames.Binds, direction = "outgoing" },
                new { predicateRole = PipelineRoles.AwaitedThing.Id, direction = "outgoing" },
                new { predicateRole = PipelineRoles.HeldVigil.Id, direction = "outgoing" },
                new { predicate = ModelNames.Has, direction = "incoming" },
            },
        };
        return await LoadSnapshotAsync(selector, TimeSpan.FromSeconds(60), cancellationToken);
    }

    // Anything but a refusal to find it is read as live, so a broker that is unwell is never answered with a second vigil.
    public async Task<bool> IsVigilLiveAsync(Guid vigilId, CancellationToken cancellationToken)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10));
        var response = await client.GetAsync($"{MyceliumUrl}/api/things/{vigilId}", cancellationToken);
        return response.StatusCode != HttpStatusCode.NotFound;
    }

    private async Task ForgetAlongAsync(Guid nodeRunId, Role role, CancellationToken cancellationToken)
    {
        var selector = new
        {
            ids = new[] { nodeRunId, role.Id },
            names = new[] { ModelNames.Binds },
            traverse = new object[] { new { predicate = ModelNames.Binds, direction = "outgoing", depth = 1 } },
            includeIsAncestors = false,
            includeRelationships = true,
            relationships = new object[]
            {
                new { predicate = ModelNames.Binds, direction = "outgoing" },
                new { predicateRole = role.Id, direction = "outgoing" },
            },
        };
        var graph = await LoadSnapshotAsync(selector, TimeSpan.FromSeconds(15), cancellationToken);
        if (graph.Thing(nodeRunId) is not { } nodeRun) return;

        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(15));
        foreach (var relationship in graph.RelationshipsAlong(nodeRun, role).ToList())
        {
            var response = await client.DeleteAsync($"{MyceliumUrl}/api/relationships/{relationship.Id}", cancellationToken);
            if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
        }
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

    public async Task<StopRequested> StopRequestedAsync(Guid stopRequestId, Guid targetId, CancellationToken cancellationToken)
    {
        var selector = new
        {
            ids = new[] { stopRequestId, targetId, PipelineRoles.RunSubject.Id, PipelineRoles.PipelineRun.Id },
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
            },
        };
        var graph = await LoadSnapshotAsync(selector, TimeSpan.FromSeconds(15), cancellationToken);
        var reason = graph.Thing(stopRequestId)?.PropertyString(ModelNames.Reason) is { Length: > 0 } stated ? stated : "Stopped by request.";
        if (graph.Thing(targetId) is not { } target) return new StopRequested(stopRequestId, reason, []);
        if (graph.IsOf(target, PipelineRoles.PipelineRun)) return new StopRequested(stopRequestId, reason, [target.Id]);

        var going = graph.IncomingAlong(target, PipelineRoles.RunSubject)
            .Where(run => run.PropertyString(ModelNames.Status) is RunStatus.Running or RunStatus.Waiting or RunStatus.Trying)
            .Select(run => run.Id)
            .ToList();
        return new StopRequested(stopRequestId, reason, going);
    }

    public Task RelateStoppedByAsync(Guid runId, Guid stopRequestId, CancellationToken cancellationToken) =>
        RelateIfBoundAsync(runId, PipelineRoles.StoppedBy, stopRequestId, cancellationToken);

    public async Task RetireVigilAsync(Guid vigilId, CancellationToken cancellationToken)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(15));
        var response = await client.DeleteAsync($"{MyceliumUrl}/api/vigils/{vigilId}", cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
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
        await CreateThingWithIdAsync(messageId, $"Message to {telling.SystemName} {runId:N}",
            new Dictionary<string, string?> { [ModelNames.Payload] = payload.GetRawText() }, cancellationToken);
        await RelateAsync(messageId, ModelNames.Is, await BoundAsync(PipelineRoles.SentMessage, cancellationToken), cancellationToken);
        await RelateAsync(runId, ModelNames.Has, messageId, cancellationToken);
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
        var response = await client.PostAsync($"{MyceliumUrl}/api/subscriptions",
            new StringContent(JsonSerializer.Serialize(selector), Encoding.UTF8, "application/json"), cancellationToken);
        response.EnsureSuccessStatusCode();

        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        // Tidy up the snapshot subscription — Phloem reads once and does not stream here.
        if (TryGetPropertyCaseInsensitive(root, "subscriptionId", out var subId) && subId.ValueKind == JsonValueKind.String)
            _ = TryUnsubscribeAsync(subId.GetString()!);

        return root;
    }

    // Read on every look, so it asks for the cadences alone; a kind of cadence is not one.
    public async Task<IReadOnlyList<Cadence>> CadencesAsync(CancellationToken cancellationToken)
    {
        var selector = new { boundTypes = new[] { PipelineRoles.Cadence.Id }, includeIsAncestors = false, includeRelationships = false };
        var root = await SnapshotAsync(selector, TimeSpan.FromSeconds(15), cancellationToken);
        var snapshot = root.Deserialize<SubscribeResult>(SnapshotJson)?.Snapshot ?? root.Deserialize<SnapshotDocument>(SnapshotJson);

        var cadences = new List<Cadence>();
        foreach (var thing in snapshot?.Things ?? [])
        {
            if (thing.IsArchetype) continue;
            var values = thing.ValuesStated().ToDictionary(value => value.Key, value => value.Value.Value, StringComparer.OrdinalIgnoreCase);
            cadences.Add(new Cadence(thing.Id, thing.Name ?? string.Empty,
                values.TryGetValue(ModelNames.IntervalMinutes, out var interval) ? NumberOf(interval) : 0,
                values.TryGetValue(ModelNames.LastDue, out var lastDue) ? InstantOf(lastDue) : null));
        }
        return cadences;
    }

    private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web);

    private static double NumberOf(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => 0,
    };

    private static DateTimeOffset? InstantOf(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant)
            ? instant
            : null;

    public Task SetLastDueAsync(Guid cadenceId, DateTimeOffset due, CancellationToken cancellationToken) =>
        SetPropertyAsync(cadenceId, ModelNames.LastDue, due.ToString("o"), cancellationToken, "vos.DateTime");

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

    // A property reaches the broker as a typed envelope, never as a bare value: the create route reads each
    // one through the same converter the property routes use, and refuses a bare value with a 400 carrying
    // no body to say why.
    private async Task CreateThingWithIdAsync(Guid id, string name, IReadOnlyDictionary<string, string?> properties, CancellationToken cancellationToken)
    {
        var envelopes = properties.ToDictionary(
            property => property.Key,
            property => new { typeInfo = PropertyType, value = property.Value });

        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(15));
        var response = await client.PostAsync($"{MyceliumUrl}/api/things",
            JsonContent.Create(new { id, name, properties = envelopes }), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

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

    private async Task RelateAsync(Guid subjectId, string predicateName, Guid targetId, CancellationToken cancellationToken) =>
        await RelateAsync(subjectId, await ResolveByNameAsync(predicateName, cancellationToken), targetId, cancellationToken);

    private async Task RelateAsync(Guid subjectId, Guid predicateId, Guid targetId, CancellationToken cancellationToken)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(15));
        var response = await client.PostAsync($"{MyceliumUrl}/api/relationships",
            JsonContent.Create(new { subjectId, predicateId, targetId }), cancellationToken);
        response.EnsureSuccessStatusCode();
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

    private async Task TryUnsubscribeAsync(string subscriptionId)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(5));
            await client.DeleteAsync($"{MyceliumUrl}/api/subscriptions/{subscriptionId}");
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to delete snapshot subscription {SubscriptionId}", subscriptionId);
        }
    }
}
