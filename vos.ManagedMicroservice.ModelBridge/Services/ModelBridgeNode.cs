using System.Net.Http.Json;
using System.Text.Json;
using vos.ManagedMicroservice.Shared.DagNode;

namespace vos.ManagedMicroservice.ModelBridge.Services;

/// <summary>
/// A generic bridge between a pipeline DAG and the model (User Story #5866). It closes the gap that Phloem assembles a
/// node's inputs only from wires and run params — with no path to read a model property or write one back. With
/// <c>mode = "read"</c> it outputs the value of a Thing's property (GET effective-properties); with <c>mode = "write"</c>
/// it writes its <c>value</c> input onto a Thing's property (a Fact). So a compute node can read a roll-up / anchor
/// param and write its result back, using ordinary node→node wires — no orchestrator change. The Thing id is baked into
/// the node params at seed-build time (deterministic under <c>--name</c>), so no runtime lookup is needed.
/// </summary>
public sealed class ModelBridgeNode : DagNodeService
{
    public ModelBridgeNode(IHttpClientFactory httpClientFactory, ILogger<ModelBridgeNode> logger,
        string myceliumUrl, string? serviceToken = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken)
    {
    }

    public override IReadOnlyList<PortDescriptor> Ports { get; } = new[]
    {
        PortDescriptor.Input("value", "any"),    // consumed in write mode
        PortDescriptor.Output("value", "any"),   // produced in read mode (and echoed in write mode)
    };

    protected override async Task<NodeResult> ExecuteNodeAsync(NodeContext context, CancellationToken cancellationToken)
    {
        var mode = context.Param("mode")?.GetString()
            ?? throw new InvalidOperationException("ModelBridge node requires a 'mode' param ('read' or 'write').");
        var thingId = ThingId(context);
        var property = context.Param("property")?.GetString()
            ?? throw new InvalidOperationException("ModelBridge node requires a 'property' param.");

        return mode switch
        {
            "read" => NodeResult.Ok(("value", await ReadAsync(thingId, property, cancellationToken))),
            "write" => await WriteAsync(thingId, property, context, cancellationToken),
            _ => throw new InvalidOperationException($"Unknown ModelBridge mode '{mode}' (expected 'read' or 'write')."),
        };
    }

    private static Guid ThingId(NodeContext context)
    {
        var raw = context.Param("thingId")
            ?? throw new InvalidOperationException("ModelBridge node requires a 'thingId' param.");
        if (raw.ValueKind == JsonValueKind.String && Guid.TryParse(raw.GetString(), out var parsed)) return parsed;
        if (raw.TryGetGuid(out var g)) return g;
        throw new InvalidOperationException("ModelBridge 'thingId' param is not a valid GUID.");
    }

    private async Task<object?> ReadAsync(Guid thingId, string property, CancellationToken cancellationToken)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10));
        var response = await client.GetAsync($"{MyceliumUrl}/api/things/{thingId}/effective-properties", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"ModelBridge read {thingId}.{property} failed ({(int)response.StatusCode} {response.StatusCode})");

        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        if (root.ValueKind == JsonValueKind.Object)
            foreach (var prop in root.EnumerateObject())
                if (string.Equals(prop.Name, property, StringComparison.OrdinalIgnoreCase))
                    return ExtractValue(prop.Value);

        throw new KeyNotFoundException($"Property '{property}' not found on thing {thingId}");
    }

    private async Task<NodeResult> WriteAsync(Guid thingId, string property, NodeContext context, CancellationToken cancellationToken)
    {
        var value = context.Input("value")
            ?? throw new InvalidOperationException("ModelBridge write requires a 'value' input.");

        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10));
        var path = $"{MyceliumUrl}/api/things/{thingId}/properties/{Uri.EscapeDataString(property)}/facts";
        var response = await client.PostAsJsonAsync(path, new { value }, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"ModelBridge write {thingId}.{property} failed ({(int)response.StatusCode} {response.StatusCode})");

        return NodeResult.Ok(("value", Unwrap(value)));
    }

    // effective-properties returns each property as { "Value": <v>, ... } (case-insensitive key).
    private static object? ExtractValue(JsonElement propertyEnvelope)
    {
        if (propertyEnvelope.ValueKind == JsonValueKind.Object)
            foreach (var field in propertyEnvelope.EnumerateObject())
                if (string.Equals(field.Name, "Value", StringComparison.OrdinalIgnoreCase))
                    return Unwrap(field.Value);
        return Unwrap(propertyEnvelope);
    }

    private static object? Unwrap(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.TryGetInt64(out var l) ? l : value.GetDouble(),
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => value.Clone(),
    };
}
