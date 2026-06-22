using System.Text.Json;
using System.Text.Json.Serialization;

namespace vos.ManagedMicroservice.Shared.DagNode;

/// <summary>
/// One input/output port a service advertises when it acts as a DAG node (Feature #5628). The
/// shape mirrors the model's <c>Port</c> archetype (direction / type / portName / required) so a
/// service's <c>/manifest</c> can seed those Port Things, and the Trellis editor can type-check wires.
/// </summary>
public sealed record PortDescriptor(
    [property: JsonPropertyName("portName")] string PortName,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("required")] bool Required = false)
{
    public static PortDescriptor Input(string portName, string type, bool required = false) =>
        new(portName, "in", type, required);

    public static PortDescriptor Output(string portName, string type) =>
        new(portName, "out", type);
}

/// <summary>
/// The resolved invocation a node's business logic runs against: run/node identity, the node's static
/// <c>params</c>, and its <c>inputs</c> keyed by input-port name. Any graph references in the wire
/// inputs have already been fetched, so every value here is a concrete <see cref="JsonElement"/>.
/// </summary>
public sealed class NodeContext
{
    public Guid RunId { get; }
    public Guid NodeId { get; }
    public JsonElement Params { get; }
    public IReadOnlyDictionary<string, JsonElement> Inputs { get; }

    public NodeContext(Guid runId, Guid nodeId, JsonElement @params, IReadOnlyDictionary<string, JsonElement> inputs)
    {
        RunId = runId;
        NodeId = nodeId;
        Params = @params;
        Inputs = inputs;
    }

    /// <summary>The value wired into <paramref name="portName"/>, or null if nothing feeds that port.</summary>
    public JsonElement? Input(string portName) =>
        Inputs.TryGetValue(portName, out var value) ? value : null;

    /// <summary>The static param named <paramref name="name"/>, or null if unset.</summary>
    public JsonElement? Param(string name) =>
        Params.ValueKind == JsonValueKind.Object && Params.TryGetProperty(name, out var value) ? value : null;
}

/// <summary>The result a node returns: output-port values, by port name. Values are literals (in-band)
/// or graph references the orchestrator routes downstream.</summary>
public sealed class NodeResult
{
    public IReadOnlyDictionary<string, object?> Outputs { get; }

    private NodeResult(IReadOnlyDictionary<string, object?> outputs) => Outputs = outputs;

    public static readonly NodeResult Empty = new(new Dictionary<string, object?>());

    public static NodeResult Ok(IReadOnlyDictionary<string, object?> outputs) => new(outputs);

    public static NodeResult Ok(params (string Port, object? Value)[] outputs) =>
        new(outputs.ToDictionary(o => o.Port, o => o.Value, StringComparer.Ordinal));
}

/// <summary>The parsed orchestrator envelope before reference inputs are resolved: run/node identity, the
/// node's static <c>params</c>, and its raw <c>inputs</c> (literals or <c>{"ref":{thingId,property}}</c>).</summary>
public sealed record NodeRequest(
    Guid RunId,
    Guid NodeId,
    JsonElement Params,
    IReadOnlyDictionary<string, JsonElement> Inputs);

/// <summary>The wire-level reply the orchestrator reads back from a node's <c>/handle</c>.</summary>
public sealed record NodeResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("outputs")] IReadOnlyDictionary<string, object?> Outputs,
    [property: JsonPropertyName("error")] string? Error);
