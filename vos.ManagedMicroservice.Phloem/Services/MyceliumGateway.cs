using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using vos.ManagedMicroservice.Phloem.Configuration;
using vos.ManagedMicroservice.Phloem.Execution;
using vos.ManagedMicroservice.Phloem.Model;
using vos.ManagedMicroservice.Shared;

namespace vos.ManagedMicroservice.Phloem.Services;

/// <summary>The HTTP implementation of <see cref="IMyceliumGateway"/> over Mycelium's API: loads the pipeline
/// subgraph via a subscription snapshot, persists run state through the thing/relationship API, and dispatches
/// nodes through the endpoint-forward route. Also registers Phloem as a managed microservice.</summary>
public sealed class MyceliumGateway : MyceliumClientBase, IMyceliumGateway
{
    private readonly ConcurrentDictionary<string, Guid> _thingIdByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, byte> _createdNodeRuns = new();
    private readonly PipelineModelOptions _model;

    public MyceliumGateway(IHttpClientFactory httpClientFactory, ILogger<MyceliumGateway> logger, string myceliumUrl, PipelineModelOptions model, string? serviceToken = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken)
    {
        _model = model;
    }

    public Task<bool> RegisterAsync(int port) => RegisterAsync(port, "Phloem", "endpoint-service");

    public async Task<PipelineGraph> LoadPipelineSubgraphAsync(Guid pipelineId, CancellationToken cancellationToken)
    {
        // Pull the whole structural closure in one snapshot. The selector explicitly includes the built-in
        // is/has predicate Things (by name) and every Port/PipelineWire Thing (by archetype) because
        // includeRelationships does NOT pull predicate Things, and includeIsAncestors does NOT pull a
        // prototype's has-children (its ports). See SelectorResolver.
        var selector = new
        {
            ids = new[] { pipelineId },
            names = new[] { ModelNames.Is, ModelNames.Has },
            types = new[] { _model.Port, _model.PipelineWire },
            traverse = new[] { new { predicate = ModelNames.Has, direction = "outgoing", depth = 8 } },
            includeIsAncestors = true,
            includeRelationships = true,
        };

        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(30));
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

    public async Task CreateRunAsync(Guid runId, Guid pipelineId, CancellationToken cancellationToken)
    {
        await CreateThingWithIdAsync(runId, $"PipelineRun {runId:N}", new Dictionary<string, object?>
        {
            ["status"] = RunStatus.Running,
            ["pipelineId"] = pipelineId.ToString(),
            ["startedUtc"] = DateTime.UtcNow.ToString("o"),
        }, cancellationToken);

        await RelateAsync(runId, "is", await ResolveByNameAsync(_model.PipelineRun, cancellationToken), cancellationToken);
        await RelateAsync(runId, "of", pipelineId, cancellationToken);
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
            await RelateAsync(nodeRunId, "is", await ResolveByNameAsync(_model.NodeRun, cancellationToken), cancellationToken);
            await RelateAsync(runId, "has", nodeRunId, cancellationToken);
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

    /// <summary>A stable id per (run, node) so a node's NodeRun is one Thing across its running→terminal
    /// transitions — required for the SSE animation to see property changes, not duplicate creates.</summary>
    private static Guid DeterministicGuid(Guid runId, Guid nodeId)
    {
        Span<byte> buffer = stackalloc byte[32];
        runId.TryWriteBytes(buffer[..16]);
        nodeId.TryWriteBytes(buffer[16..]);
        return new Guid(System.Security.Cryptography.MD5.HashData(buffer));
    }

    /// <summary>Per-item NodeRun id for a fan-out — stable per (run, node, item index) (#5648).</summary>
    private static Guid DeterministicGuid(Guid runId, Guid nodeId, int index)
    {
        Span<byte> buffer = stackalloc byte[36];
        runId.TryWriteBytes(buffer[..16]);
        nodeId.TryWriteBytes(buffer[16..32]);
        BitConverter.TryWriteBytes(buffer[32..], index);
        return new Guid(System.Security.Cryptography.MD5.HashData(buffer));
    }

    /// <summary>A property value arrives wrapped as <c>{ value|Value, type }</c>; return the bare value.</summary>
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
