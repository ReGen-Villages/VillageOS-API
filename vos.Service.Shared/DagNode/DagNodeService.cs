using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace vos.Service.Shared.DagNode;

// Base a microservice inherits to act as a node in a pipeline DAG (Feature #5628). It maps the uniform
// orchestrator envelope {runId,nodeId,params,inputs} → {success,outputs,error} onto the
// subclass's ExecuteNodeAsync, resolving any graph-reference inputs first. The envelope is
// additive: a service detects a node invocation with IsNodeEnvelope and routes it
// here, leaving its existing graph/http /handle behaviour untouched. Ports backs the
// optional /manifest endpoint and the Trellis palette.
public abstract class DagNodeService : MyceliumClientBase
{
    protected DagNodeService(IHttpClientFactory httpClientFactory, ILogger logger, string myceliumUrl, string? serviceToken = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken)
    {
    }

    // The input/output ports this node advertises — served at /manifest and shown in the editor.
    public abstract IReadOnlyList<PortDescriptor> Ports { get; }

    // Map resolved inputs + params to outputs. Throw to fail the node — the failure is reported as
    // {success:false,error}, never as an unhandled 500.
    protected abstract Task<NodeResult> ExecuteNodeAsync(NodeContext context, CancellationToken cancellationToken);

    // True iff root is a DAG-node invocation (carries both runId and
    // nodeId) rather than a legacy graph/http /handle body.
    public static bool IsNodeEnvelope(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("runId", out _)
        && root.TryGetProperty("nodeId", out _);

    // Parse the envelope, resolve reference inputs, run the node, and shape the reply. A service
    // wires this into its /handle after IsNodeEnvelope matches.
    public async Task<NodeResponse> HandleNodeAsync(JsonElement root, CancellationToken cancellationToken = default)
    {
        NodeRequest request;
        try
        {
            request = ParseEnvelope(root);
        }
        catch (Exception ex)
        {
            return Failure($"Malformed node envelope: {ex.Message}");
        }

        try
        {
            var inputs = await ResolveInputsAsync(request.Inputs, cancellationToken);
            var context = new NodeContext(request.RunId, request.NodeId, request.Params, inputs);
            var result = await ExecuteNodeAsync(context, cancellationToken);
            return new NodeResponse(true, result.Outputs, null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "DAG node {NodeId} (run {RunId}) failed", request.NodeId, request.RunId);
            return Failure(ex.Message);
        }
    }

    private static NodeRequest ParseEnvelope(JsonElement root)
    {
        var runId = root.GetProperty("runId").GetGuid();
        var nodeId = root.GetProperty("nodeId").GetGuid();
        var @params = root.TryGetProperty("params", out var p) ? p.Clone() : default;

        var inputs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (root.TryGetProperty("inputs", out var ins) && ins.ValueKind == JsonValueKind.Object)
            foreach (var prop in ins.EnumerateObject())
                inputs[prop.Name] = prop.Value.Clone();

        return new NodeRequest(runId, nodeId, @params, inputs);
    }

    // Replace any {"ref":{"thingId","property"}} input with the live graph value; pass literals through.
    private async Task<IReadOnlyDictionary<string, JsonElement>> ResolveInputsAsync(
        IReadOnlyDictionary<string, JsonElement> inputs, CancellationToken cancellationToken)
    {
        var resolved = new Dictionary<string, JsonElement>(inputs.Count, StringComparer.Ordinal);
        foreach (var (port, value) in inputs)
            resolved[port] = TryReadRef(value, out var thingId, out var property)
                ? await FetchRefAsync(thingId, property, cancellationToken)
                : value;
        return resolved;
    }

    private static bool TryReadRef(JsonElement value, out Guid thingId, out string property)
    {
        thingId = Guid.Empty;
        property = string.Empty;
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("ref", out var r) || r.ValueKind != JsonValueKind.Object)
            return false;
        if (!r.TryGetProperty("thingId", out var t) || !t.TryGetGuid(out thingId))
            return false;
        if (!r.TryGetProperty("property", out var p) || p.ValueKind != JsonValueKind.String)
            return false;
        property = p.GetString()!;
        return true;
    }

    private async Task<JsonElement> FetchRefAsync(Guid thingId, string property, CancellationToken cancellationToken)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10));
        var response = await client.GetAsync($"{MyceliumUrl}{MyceliumRoutes.ThingProperties(thingId)}", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Failed to resolve input ref {thingId}.{property} ({(int)response.StatusCode} {response.StatusCode})",
                null, response.StatusCode);

        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        if (root.ValueKind == JsonValueKind.Object)
            foreach (var prop in root.EnumerateObject())
                if (string.Equals(prop.Name, property, StringComparison.OrdinalIgnoreCase))
                    return ExtractValue(prop.Value);

        throw new KeyNotFoundException($"Property '{property}' not found on thing {thingId}");
    }

    // The route returns each property as { "Value": <v>, ... } (case-insensitive key).
    private static JsonElement ExtractValue(JsonElement propertyEnvelope)
    {
        if (propertyEnvelope.ValueKind == JsonValueKind.Object)
            foreach (var field in propertyEnvelope.EnumerateObject())
                if (string.Equals(field.Name, "Value", StringComparison.OrdinalIgnoreCase))
                    return field.Value.Clone();
        return propertyEnvelope.Clone();
    }

    private static NodeResponse Failure(string error) => new(false, NodeResult.Empty.Outputs, error);
}
