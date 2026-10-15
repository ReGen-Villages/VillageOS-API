using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Shared;

namespace vos.Service.Phloem.Services;

// The HTTP implementation of IMyceliumGateway over Mycelium's API: loads the pipeline
// subgraph via a subscription snapshot, persists run state through the thing/relationship API, and dispatches
// nodes through the endpoint-forward route. Also registers Phloem as a managed microservice.
public sealed class MyceliumGateway : MyceliumClientBase, IMyceliumGateway
{
    // Everything this gateway records about a run is text, so one type name serves every property it writes.
    private const string PropertyType = "vos.String";

    private readonly ConcurrentDictionary<string, Guid> _thingIdByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, Guid> _boundByRole = new();
    private readonly ConcurrentDictionary<Guid, byte> _createdNodeRuns = new();

    public MyceliumGateway(IHttpClientFactory httpClientFactory, ILogger<MyceliumGateway> logger, string myceliumUrl, string? serviceToken = null, string? apiKey = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken, apiKey: apiKey) { }

    public Task<bool> RegisterAsync(int port) => RegisterAsync(port, "Phloem", "endpoint-service");

    // A role is read off the drawing only when its role Thing is in it.
    private static readonly IReadOnlyList<Role> DrawingRoles =
        [.. PipelineRoles.DagRoles, PipelineRoles.ExternalSystem, PipelineRoles.StandsFor, PipelineRoles.ToldThrough, PipelineRoles.Choice,
         PipelineRoles.CompletionRange, PipelineRoles.AwaitsThrough];

    private static readonly IReadOnlyList<Role> StartRoles =
    [
        PipelineRoles.Pipeline, PipelineRoles.PipelineInput, PipelineRoles.Connection,
        PipelineRoles.StateWatch, PipelineRoles.StandsFor, PipelineRoles.PipelineStart,
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
            },
            includeIsAncestors = true,
            includeRelationships = true,
        };

        return await LoadSnapshotAsync(selector, TimeSpan.FromSeconds(30), cancellationToken);
    }

    public async Task<PipelineGraph> LoadStartSubgraphAsync(Guid targetId, CancellationToken cancellationToken)
    {
        // The target and what tells it apart: its `is` chain, the state it watches, the start nodes standing
        // for either and the pipelines holding them, and any pipeline it reaches along the start role. The role
        // Things come with what each binds so the graph can tell what plays each role, and the traversals run in
        // order, so the watched state is reached before the nodes standing for it.
        // Relationship rules keep the read to those edges: a state connection gains one dispatch record per
        // Thing that ever entered its state, and the wider reading would carry every one on every start.
        var selector = new
        {
            ids = StartRoles.Select(role => role.Id).Prepend(targetId).ToArray(),
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
                new { predicate = ModelNames.Has, direction = "incoming" },
                new { predicateRole = PipelineRoles.PipelineStart.Id, direction = "outgoing" },
            },
        };

        return await LoadSnapshotAsync(selector, TimeSpan.FromSeconds(15), cancellationToken);
    }

    public async Task CreateRunAsync(
        Guid runId, Guid pipelineId, CancellationToken cancellationToken, RunSubject? subject = null, Guid? requestId = null,
        JsonElement? parameters = null)
    {
        // The result, the error and the finish are declared here with no value. Each is known only once the
        // run ends, and the property route sets a property the Thing already carries — so a run given one
        // only at the end is a run that can never be given one.
        var properties = new Dictionary<string, string?>
        {
            ["status"] = RunStatus.Running,
            ["pipelineId"] = pipelineId.ToString(),
            [ModelNames.StartedUtc] = DateTime.UtcNow.ToString("o"),
            [ModelNames.FinishedUtc] = null,
            [ModelNames.Parameters] = parameters is { ValueKind: not JsonValueKind.Undefined } given ? given.GetRawText() : null,
            [ModelNames.Result] = null,
            [ModelNames.Error] = null,
        };
        if (requestId is { } request) properties[ModelNames.RequestId] = request.ToString();
        await CreateThingWithIdAsync(runId, $"PipelineRun {runId:N}", properties, cancellationToken);

        await RelateAsync(runId, ModelNames.Is, await BoundAsync(PipelineRoles.PipelineRun, cancellationToken), cancellationToken);
        await RelateAsync(runId, ModelNames.Of, pipelineId, cancellationToken);

        if (subject is null) return;
        var subjectPredicateId = await TryBoundAsync(PipelineRoles.RunSubject, cancellationToken);
        if (subjectPredicateId is null)
        {
            Logger.LogWarning("The model binds nothing to the role {Role}; run {RunId} is recorded without its subject {SubjectId}",
                PipelineRoles.RunSubject.Word, runId, subject.Id);
            return;
        }
        await RelateAsync(runId, subjectPredicateId.Value, subject.Id, cancellationToken);
    }

    public async Task SetNodeRunStatusAsync(Guid runId, Guid nodeId, string nodeName, string status, string? error, CancellationToken cancellationToken, int? index = null, int total = 0,
        IReadOnlyDictionary<string, JsonElement>? outputs = null)
    {
        var nodeRunId = index is int i ? DeterministicGuid(runId, nodeId, i) : DeterministicGuid(runId, nodeId);
        var terminal = status != RunStatus.Running;
        var now = DateTime.UtcNow.ToString("o");
        if (_createdNodeRuns.TryAdd(nodeRunId, 0))
        {
            // First status for this NodeRun — create the Thing and wire it into the run, declaring every
            // property a later transition writes.
            var properties = new Dictionary<string, string?>
            {
                ["status"] = status,
                ["nodeId"] = nodeId.ToString(),
                [ModelNames.Error] = error,
                [ModelNames.StartedUtc] = now,
                [ModelNames.FinishedUtc] = terminal ? now : null,
                [ModelNames.Outputs] = outputs is null ? null : OutputsText(outputs),
                [ModelNames.Attempts] = null,
            };
            if (index is int idx)
            {
                properties["index"] = idx.ToString();
                properties["total"] = total.ToString();
            }
            await CreateThingWithIdAsync(nodeRunId, index is int x ? $"NodeRun {nodeName} #{x}" : $"NodeRun {nodeName}", properties, cancellationToken);
            await RelateAsync(nodeRunId, ModelNames.Is, await BoundAsync(PipelineRoles.NodeRun, cancellationToken), cancellationToken);
            await RelateAsync(runId, ModelNames.Has, nodeRunId, cancellationToken);
            return;
        }

        // Subsequent transition (running -> terminal) on the same Thing — a property change the SSE view sees.
        await SetPropertyAsync(nodeRunId, "status", status, cancellationToken);
        if (error != null) await SetPropertyAsync(nodeRunId, ModelNames.Error, error, cancellationToken);
        if (terminal) await SetPropertyAsync(nodeRunId, ModelNames.FinishedUtc, now, cancellationToken);
        if (outputs is not null) await SetPropertyAsync(nodeRunId, ModelNames.Outputs, OutputsText(outputs), cancellationToken);
    }

    private static string OutputsText(IReadOnlyDictionary<string, JsonElement> outputs) =>
        JsonSerializer.Serialize(outputs);

    public async Task SetRunStatusAsync(Guid runId, string status, CancellationToken cancellationToken, string? error = null)
    {
        await SetPropertyAsync(runId, "status", status, cancellationToken);
        if (error != null) await SetPropertyAsync(runId, ModelNames.Error, error, cancellationToken);
        if (status != RunStatus.Running) await SetPropertyAsync(runId, ModelNames.FinishedUtc, DateTime.UtcNow.ToString("o"), cancellationToken);
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

    public async Task<bool> IsCancelRequestedAsync(Guid runId, CancellationToken cancellationToken)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10));
        var response = await client.GetAsync($"{MyceliumUrl}/api/things/{runId}", cancellationToken);
        if (!response.IsSuccessStatusCode) return false;

        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        if (TryGetPropertyCaseInsensitive(root, "Properties", out var props) && props.ValueKind == JsonValueKind.Object
            && props.TryGetProperty("cancelRequested", out var cr))
        {
            return Truth.IsTrue(Unwrap(cr));
        }
        return false;
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

    // Related to the system last: that write is the one the broker dispatches, and the sender it reaches reads
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

    public async Task<NodeDispatchResult> DispatchAsync(string subdomain, JsonElement envelope, CancellationToken cancellationToken)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromMinutes(5));
        var response = await client.PostAsync($"{MyceliumUrl}/api/endpoints/{Uri.EscapeDataString(subdomain)}",
            new StringContent(envelope.GetRawText(), Encoding.UTF8, "application/json"), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new NodeDispatchResult((int)response.StatusCode, body);
    }

    private async Task<PipelineGraph> LoadSnapshotAsync(object selector, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var client = await CreateAuthenticatedClientAsync(timeout);
        var response = await client.PostAsync($"{MyceliumUrl}/api/subscriptions",
            new StringContent(JsonSerializer.Serialize(selector), Encoding.UTF8, "application/json"), cancellationToken);
        response.EnsureSuccessStatusCode();

        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var graph = SnapshotParser.Parse(root);

        // Tidy up the snapshot subscription — Phloem reads once and does not stream here.
        if (TryGetPropertyCaseInsensitive(root, "subscriptionId", out var subId) && subId.ValueKind == JsonValueKind.String)
            _ = TryUnsubscribeAsync(subId.GetString()!);

        return graph;
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

    private async Task SetPropertyAsync(Guid thingId, string name, string value, CancellationToken cancellationToken)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(15));
        var response = await client.PutAsync($"{MyceliumUrl}/api/things/{thingId}/properties",
            JsonContent.Create(new { name, type = PropertyType, value }), cancellationToken);
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

    // A property value comes wrapped as { value, typeInfo }.
    private static JsonElement Unwrap(JsonElement value) =>
        TryGetPropertyCaseInsensitive(value, "value", out var bare) ? bare : value;

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
