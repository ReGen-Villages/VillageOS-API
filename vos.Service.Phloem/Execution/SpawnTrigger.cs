using System.Text.Json;

namespace vos.Service.Phloem.Execution;

public enum SpawnKind
{
    // http endpoint-forward: { pipelineId, params } — synchronous spawn-and-wait.
    Http,
    // A relationship the broker dispatched: a `X runs Pipeline` write, or a Thing entering a watched state.
    // Which pipeline it starts is read from the model, not from the body — fire-and-forget.
    Relationship,
    Invalid,
}

// Classifies a /handle body into a spawn. Phloem is reachable two ways through Mycelium, both via the
// same /handle: an http endpoint-forward carrying {pipelineId, params} (Trellis Run, or any caller), or a
// relationship the broker dispatches as {relationshipId, subjectId, targetId, subjectName, targetName,
// properties}, whose properties are the run params. The body alone cannot say what the target starts —
// PipelineStart reads that from the model — so it travels as it came, with the subject.
// Detected by shape — pipelineId ⇒ http, else targetId ⇒ relationship.
// Async: for an http spawn, {"async":true} asks Phloem to return the run id immediately and run the DAG in
// the background (the editor animates over SSE) instead of blocking for the result. A relationship is
// always fire-and-forget.
public sealed record SpawnTrigger(
    SpawnKind Kind, Guid PipelineId, Guid TargetId, RunSubject? Subject, JsonElement Params, bool Async, string? Error)
{
    public static SpawnTrigger Resolve(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return Invalid("Request body must be a JSON object.");

        if (root.TryGetProperty("pipelineId", out var pid))
            return pid.ValueKind == JsonValueKind.String && Guid.TryParse(pid.GetString(), out var httpId)
                ? new SpawnTrigger(SpawnKind.Http, httpId, Guid.Empty, null, CloneProp(root, "params"), IsAsync(root), null)
                : Invalid("'pipelineId' must be a guid.");

        if (root.TryGetProperty("targetId", out var tid))
            return tid.ValueKind == JsonValueKind.String && Guid.TryParse(tid.GetString(), out var targetId)
                ? new SpawnTrigger(SpawnKind.Relationship, Guid.Empty, targetId, SubjectOf(root), CloneProp(root, "properties"), true, null)
                : Invalid("'targetId' must be a guid.");

        return Invalid("Request must include 'pipelineId' (http spawn) or 'targetId' (a dispatched relationship).");
    }

    private static RunSubject? SubjectOf(JsonElement root)
    {
        if (!root.TryGetProperty("subjectId", out var sid) || sid.ValueKind != JsonValueKind.String
            || !Guid.TryParse(sid.GetString(), out var subjectId))
            return null;
        var name = root.TryGetProperty("subjectName", out var sn) && sn.ValueKind == JsonValueKind.String
            ? sn.GetString() ?? string.Empty
            : string.Empty;
        return new RunSubject(subjectId, name);
    }

    private static bool IsAsync(JsonElement root) =>
        root.TryGetProperty("async", out var a) && a.ValueKind == JsonValueKind.True;

    private static JsonElement CloneProp(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) ? v.Clone() : default;

    private static SpawnTrigger Invalid(string error) => new(SpawnKind.Invalid, Guid.Empty, Guid.Empty, null, default, false, error);
}
