using System.Text.Json;
using vos.Service.Shared;
using vos.Service.Shared.DagNode;

namespace vos.Service.ModelBridge.Services;

// A step between a pipeline and the model. Phloem fills a step's inputs only from wires and run
// parameters, with no way to read a model property or write one back. With mode = "read" this outputs the
// value of a Thing's property (from the Thing's properties); with mode = "write" it writes its value input
// onto a Thing's property as a Fact. So a compute step can read a derived property or an anchor parameter
// and write its result back over ordinary wires between steps, with no change to Phloem. The Thing's id is
// written into the step's parameters when the seed is built (the same under --name), so nothing is looked
// up at run time.
public sealed class ModelBridgeNode : DagNodeService
{
    public ModelBridgeNode(IHttpClientFactory httpClientFactory, ILogger<ModelBridgeNode> logger,
        string myceliumUrl, LaunchToken? launchToken = null, string? apiKey = null)
        : base(httpClientFactory, logger, myceliumUrl, launchToken, apiKey: apiKey)
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
            "read" => NodeResult.Ok(("value", Unwrap(await ReadPropertyAsync(thingId, property, cancellationToken)))),
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

    private async Task<NodeResult> WriteAsync(Guid thingId, string property, NodeContext context, CancellationToken cancellationToken)
    {
        var value = context.Input("value")
            ?? throw new InvalidOperationException("ModelBridge write requires a 'value' input.");

        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10));
        var path = $"{MyceliumUrl}{MyceliumRoutes.ThingPropertyFacts(thingId, property)}";
        var response = await client.PostAsJsonAsync(path, new { value }, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"ModelBridge write {thingId}.{property} failed ({(int)response.StatusCode} {response.StatusCode})");

        return NodeResult.Ok(("value", Unwrap(value)));
    }

    private static object? Unwrap(JsonElement value) =>
        JsonScalar.TryRead(value, out var scalar) ? scalar : value.Clone();
}
