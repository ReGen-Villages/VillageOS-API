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

    public async Task PersistNodeRunAsync(Guid runId, NodeRunResult node, CancellationToken cancellationToken)
    {
        var nodeRunId = Guid.NewGuid();
        await CreateThingWithIdAsync(nodeRunId, $"NodeRun {node.Name}", new Dictionary<string, object?>
        {
            ["status"] = node.Status,
            ["nodeId"] = node.NodeId.ToString(),
            ["error"] = node.Error,
        }, cancellationToken);
        await RelateAsync(nodeRunId, "is", await ResolveByNameAsync(_model.NodeRun, cancellationToken), cancellationToken);
        await RelateAsync(runId, "has", nodeRunId, cancellationToken);
    }

    public async Task SetRunStatusAsync(Guid runId, string status, CancellationToken cancellationToken)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(15));
        var response = await client.PutAsync($"{MyceliumUrl}/api/things/{runId}/properties",
            JsonContent.Create(new { name = "status", type = "vos.String", value = status }), cancellationToken);
        response.EnsureSuccessStatusCode();
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
