using System.Text.Json;
using System.Text.Json.Serialization;

namespace vos.Service.Shared.DagNode;

// One input/output port a service advertises when it acts as a DAG node. The
// shape mirrors the model's Port archetype (direction / type / portName / required) so a
// service's /manifest can seed those Port Things, and the Trellis editor can type-check wires.
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

// The resolved invocation a node's business logic runs against: run/node identity, the node's static
// params, and its inputs keyed by input-port name. Any graph references in the wire
// inputs have already been fetched, so every value here is a concrete JsonElement.
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

    public JsonElement? Input(string portName) =>
        Inputs.TryGetValue(portName, out var value) ? value : null;

    public JsonElement? Param(string name) =>
        Params.ValueKind == JsonValueKind.Object && Params.TryGetProperty(name, out var value) ? value : null;
}

// The result a node returns: output-port values, by port name. Values are literals (in-band)
// or graph references the orchestrator routes downstream. Decision names one of the decision paths the service
// declares, when the node decides.
public sealed class NodeResult
{
    public IReadOnlyDictionary<string, object?> Outputs { get; }
    public string? Decision { get; }

    private NodeResult(IReadOnlyDictionary<string, object?> outputs, string? decision = null)
    {
        Outputs = outputs;
        Decision = decision;
    }

    public static readonly NodeResult Empty = new(new Dictionary<string, object?>());

    public static NodeResult Ok(IReadOnlyDictionary<string, object?> outputs) => new(outputs);

    public static NodeResult Ok(params (string Port, object? Value)[] outputs) =>
        new(outputs.ToDictionary(o => o.Port, o => o.Value, StringComparer.Ordinal));

    public static NodeResult Decided(string decision, params (string Port, object? Value)[] outputs) =>
        new(outputs.ToDictionary(o => o.Port, o => o.Value, StringComparer.Ordinal), decision);
}

// The parsed orchestrator envelope before reference inputs are resolved: run/node identity, the
// node's static params, and its raw inputs (literals or {"ref":{thingId,property}}).
public sealed record NodeRequest(
    Guid RunId,
    Guid NodeId,
    JsonElement Params,
    IReadOnlyDictionary<string, JsonElement> Inputs);

public sealed record NodeResponse(
    [property: JsonPropertyName("result")] string Result,
    [property: JsonPropertyName("outputs")] IReadOnlyDictionary<string, object?> Outputs,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("decision"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Decision = null)
{
    public const string Succeeded = "success";
    public const string Failed = "fail";
}
