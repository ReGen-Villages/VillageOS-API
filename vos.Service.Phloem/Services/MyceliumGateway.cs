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
    private readonly ConcurrentDictionary<string, Guid> _thingIdByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Guid> _archetypeIdByRoleFlag = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, byte> _createdNodeRuns = new();

    public MyceliumGateway(IHttpClientFactory httpClientFactory, ILogger<MyceliumGateway> logger, string myceliumUrl, string? serviceToken = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken) { }

    public Task<bool> RegisterAsync(int port) => RegisterAsync(port, "Phloem", "endpoint-service");

    public async Task<PipelineGraph> LoadPipelineSubgraphAsync(Guid pipelineId, CancellationToken cancellationToken)
    {
        // Pull the whole structural closure in one snapshot. Three things the traversal will not reach on
        // its own are asked for outright: the built-in is/has predicate Things, because includeRelationships
        // does not pull predicate Things; every Port and wire Thing, because includeIsAncestors does not
        // pull a prototype's has-children; and the archetype for each role, so that one missing from the
        // snapshot means the model marks it on nothing rather than that this pipeline plays that role
        // nowhere. See SelectorResolver.
        var selector = new
        {
            ids = new[] { pipelineId },
            names = new[] { ModelNames.Is, ModelNames.Has },
            markedTypes = new[] { PipelineArchetypes.PortFlag, PipelineArchetypes.PipelineWireFlag },
            markedArchetypes = PipelineArchetypes.DagRoleFlags,
            traverse = new[] { new { predicate = ModelNames.Has, direction = "outgoing", depth = 8 } },
            includeIsAncestors = true,
            includeRelationships = true,
        };

        return await LoadSnapshotAsync(selector, TimeSpan.FromSeconds(30), cancellationToken);
    }

    public async Task CreateRunAsync(Guid runId, Guid pipelineId, CancellationToken cancellationToken)
    {
        await CreateThingWithIdAsync(runId, $"PipelineRun {runId:N}", new Dictionary<string, object?>
        {
            ["status"] = RunStatus.Running,
            ["pipelineId"] = pipelineId.ToString(),
            ["startedUtc"] = DateTime.UtcNow.ToString("o"),
        }, cancellationToken);

        await RelateAsync(runId, ModelNames.Is,
            await ArchetypeCarryingAsync(PipelineArchetypes.PipelineRunFlag, cancellationToken), cancellationToken);
        await RelateAsync(runId, ModelNames.Of, pipelineId, cancellationToken);
    }

    public async Task SetNodeRunStatusAsync(Guid runId, Guid nodeId, string nodeName, string status, string? error, CancellationToken cancellationToken, int? index = null, int total = 0)
    {
        var nodeRunId = index is int i ? DeterministicGuid(runId, nodeId, i) : DeterministicGuid(runId, nodeId);
        if (_createdNodeRuns.TryAdd(nodeRunId, 0))
        {
            // First status for this NodeRun — create the Thing and wire it into the run.
            var properties = new Dictionary<string, object?>
            {
                ["status"] = status,
                ["nodeId"] = nodeId.ToString(),
                ["error"] = error,
            };
            if (index is int idx)
            {
                properties["index"] = idx.ToString();
                properties["total"] = total.ToString();
            }
            await CreateThingWithIdAsync(nodeRunId, index is int x ? $"NodeRun {nodeName} #{x}" : $"NodeRun {nodeName}", properties, cancellationToken);
            await RelateAsync(nodeRunId, ModelNames.Is,
                await ArchetypeCarryingAsync(PipelineArchetypes.NodeRunFlag, cancellationToken), cancellationToken);
            await RelateAsync(runId, ModelNames.Has, nodeRunId, cancellationToken);
            return;
        }

        // Subsequent transition (running -> terminal) on the same Thing — a property change the SSE view sees.
        await SetPropertyAsync(nodeRunId, "status", status, cancellationToken);
        if (error != null) await SetPropertyAsync(nodeRunId, "error", error, cancellationToken);
    }

    public Task SetRunStatusAsync(Guid runId, string status, CancellationToken cancellationToken) =>
        SetPropertyAsync(runId, "status", status, cancellationToken);

    public Task SetRunResultAsync(Guid runId, JsonElement result, CancellationToken cancellationToken) =>
        SetPropertyAsync(runId, ModelNames.Result, result.GetRawText(), cancellationToken);

    public async Task<bool> IsCancelRequestedAsync(Guid runId, CancellationToken cancellationToken)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10));
        var response = await client.GetAsync($"{MyceliumUrl}/api/things/{runId}", cancellationToken);
        if (!response.IsSuccessStatusCode) return false;

        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        if (root.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object
            && props.TryGetProperty("cancelRequested", out var cr))
        {
            var v = Unwrap(cr);
            return v.ValueKind == JsonValueKind.True
                || (v.ValueKind == JsonValueKind.String && string.Equals(v.GetString(), "true", StringComparison.OrdinalIgnoreCase));
        }
        return false;
    }

    public async Task<NodeDispatchResult> DispatchAsync(string subdomain, JsonElement envelope, CancellationToken cancellationToken)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromMinutes(5));
        var response = await client.PostAsync($"{MyceliumUrl}/api/endpoints/{Uri.EscapeDataString(subdomain)}",
            new StringContent(envelope.GetRawText(), Encoding.UTF8, "application/json"), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new NodeDispatchResult((int)response.StatusCode, body);
    }

    // --- helpers ---

    private async Task<PipelineGraph> LoadSnapshotAsync(object selector, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var client = await CreateAuthenticatedClientAsync(timeout);
        var response = await client.PostAsync($"{MyceliumUrl}/api/subscriptions",
            new StringContent(JsonSerializer.Serialize(selector), Encoding.UTF8, "application/json"), cancellationToken);
        response.EnsureSuccessStatusCode();

        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var graph = SnapshotParser.Parse(root);

        // Tidy up the snapshot subscription — Phloem reads once and does not stream here.
        if (root.TryGetProperty("subscriptionId", out var subId) && subId.ValueKind == JsonValueKind.String)
            _ = TryUnsubscribeAsync(subId.GetString()!);

        return graph;
    }

    // The archetype playing a role, found by the flag it carries rather than by a name this orchestrator
    // was told (#6516). Asked for on its own: markedTypes would answer with every Thing that already `is`
    // one, which for a run archetype is every run ever recorded. An archetype cannot change role while the
    // process lives, and this runs once per node run, so the answer is kept.
    private async Task<Guid> ArchetypeCarryingAsync(string roleFlag, CancellationToken cancellationToken)
    {
        if (_archetypeIdByRoleFlag.TryGetValue(roleFlag, out var cached)) return cached;

        var selector = new
        {
            markedArchetypes = new[] { roleFlag },
            includeIsAncestors = false,
            includeRelationships = false,
        };
        var graph = await LoadSnapshotAsync(selector, TimeSpan.FromSeconds(15), cancellationToken);

        var archetype = graph.ArchetypeCarrying(roleFlag)
            ?? throw new InvalidOperationException($"The model marks no archetype with {roleFlag} = true.");
        _archetypeIdByRoleFlag[roleFlag] = archetype.Id;
        return archetype.Id;
    }

    private async Task CreateThingWithIdAsync(Guid id, string name, IReadOnlyDictionary<string, object?> properties, CancellationToken cancellationToken)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(15));
        var response = await client.PostAsync($"{MyceliumUrl}/api/things",
            JsonContent.Create(new { id, name, properties }), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task SetPropertyAsync(Guid thingId, string name, string value, CancellationToken cancellationToken)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(15));
        var response = await client.PutAsync($"{MyceliumUrl}/api/things/{thingId}/properties",
            JsonContent.Create(new { name, type = "vos.String", value }), cancellationToken);
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

    // Per-item NodeRun id for a fan-out — stable per (run, node, item index) (#5648).
    private static Guid DeterministicGuid(Guid runId, Guid nodeId, int index)
    {
        Span<byte> buffer = stackalloc byte[36];
        runId.TryWriteBytes(buffer[..16]);
        nodeId.TryWriteBytes(buffer[16..32]);
        BitConverter.TryWriteBytes(buffer[32..], index);
        return new Guid(System.Security.Cryptography.MD5.HashData(buffer));
    }

    // A property value arrives wrapped as { value|Value, type }; return the bare value.
    private static JsonElement Unwrap(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var field in value.EnumerateObject())
                if (field.NameEquals("value") || field.NameEquals("Value"))
                    return field.Value;
        return value;
    }

    private async Task RelateAsync(Guid subjectId, string predicateName, Guid targetId, CancellationToken cancellationToken)
    {
        var predicateId = await ResolveByNameAsync(predicateName, cancellationToken);
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
        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        var thing = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().FirstOrDefault() : root;
        if (thing.ValueKind == JsonValueKind.Object && thing.TryGetProperty("id", out var idEl)
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
