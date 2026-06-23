using System.Text.Json;

namespace vos.ManagedMicroservice.Phloem.Execution;

/// <summary>How a spawn reached Phloem.</summary>
public enum SpawnKind
{
    /// <summary>http endpoint-forward: <c>{ pipelineId, params }</c> — synchronous spawn-and-wait.</summary>
    Http,
    /// <summary>graph trigger: a <c>X runs Pipeline</c> relationship envelope — fire-and-forget.</summary>
    Graph,
    Invalid,
}

/// <summary>
/// Classifies a <c>/handle</c> body into a spawn (#5633). Phloem is reachable two ways through Mycelium,
/// both via the same <c>/handle</c>:
/// <list type="bullet">
/// <item>an <b>http endpoint-forward</b> carrying <c>{pipelineId, params}</c> (Trellis Run, or any caller);</item>
/// <item>a <b>graph trigger</b> — Mycelium forwards a <c>X runs Pipeline</c> relationship as
/// <c>{relationshipId, subjectId, targetId, subjectName, targetName, properties}</c>; the <c>targetId</c>
/// is the Pipeline and the relationship's <c>properties</c> are the run params.</item>
/// </list>
/// Detected by shape — <c>pipelineId</c> ⇒ http, else <c>targetId</c> ⇒ graph.
/// </summary>
/// <param name="Async">For an http spawn, <c>{"async":true}</c> asks Phloem to return the run id immediately
/// and run the DAG in the background (the editor animates over SSE) instead of blocking for the result. A graph
/// trigger is always fire-and-forget.</param>
public sealed record SpawnTrigger(SpawnKind Kind, Guid PipelineId, JsonElement Params, bool Async, string? Error)
{
    public static SpawnTrigger Resolve(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return Invalid("Request body must be a JSON object.");

        if (root.TryGetProperty("pipelineId", out var pid))
            return pid.ValueKind == JsonValueKind.String && Guid.TryParse(pid.GetString(), out var httpId)
                ? new SpawnTrigger(SpawnKind.Http, httpId, CloneProp(root, "params"), IsAsync(root), null)
                : Invalid("'pipelineId' must be a guid.");

        if (root.TryGetProperty("targetId", out var tid))
            return tid.ValueKind == JsonValueKind.String && Guid.TryParse(tid.GetString(), out var graphId)
                ? new SpawnTrigger(SpawnKind.Graph, graphId, CloneProp(root, "properties"), true, null)
                : Invalid("'targetId' must be a guid.");

        return Invalid("Request must include 'pipelineId' (http spawn) or 'targetId' (graph trigger).");
    }

    private static bool IsAsync(JsonElement root) =>
        root.TryGetProperty("async", out var a) && a.ValueKind == JsonValueKind.True;

    private static JsonElement CloneProp(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) ? v.Clone() : default;

    private static SpawnTrigger Invalid(string error) => new(SpawnKind.Invalid, Guid.Empty, default, false, error);
}
