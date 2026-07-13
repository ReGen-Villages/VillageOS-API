using System.Text.Json;

namespace vos.ManagedMicroservice.Phloem.Execution;

// How a spawn reached Phloem.
public enum SpawnKind
{
    // http endpoint-forward: { pipelineId, params } — synchronous spawn-and-wait.
    Http,
    // graph trigger: a X runs Pipeline relationship envelope — fire-and-forget.
    Graph,
    Invalid,
}

// Classifies a /handle body into a spawn (#5633). Phloem is reachable two ways through Mycelium,
// both via the same /handle:
// an http endpoint-forward carrying {pipelineId, params} (Trellis Run, or any caller);
// a graph trigger — Mycelium forwards a X runs Pipeline relationship as
// {relationshipId, subjectId, targetId, subjectName, targetName, properties}; the targetId
// is the Pipeline and the relationship's properties are the run params.
// Detected by shape — pipelineId ⇒ http, else targetId ⇒ graph.
// Async: For an http spawn, {"async":true} asks Phloem to return the run id immediately
// and run the DAG in the background (the editor animates over SSE) instead of blocking for the result. A graph
// trigger is always fire-and-forget.
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
