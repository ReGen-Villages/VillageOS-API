using System.Text.Json;

namespace vos.Service.Shared.DagNode;

// What a service should do with a body posted to /handle.
public enum HandleRequestKind
{
    // A pipeline run invoking this service as a node: hand it to the DAG-node path.
    NodeEnvelope,

    // A graph relationship naming the Thing it acts on: read that Thing's inputs, recompute,
    // and write the outputs back.
    RelationshipSubject,

    // Neither shape, so the service cannot act on it.
    Unrecognised,
}

// What a posted body turned out to be: the shape to act on, the Thing it names when it names one,
// and the parsed body for the node path to read. Json is left undefined when the text was not JSON,
// which IsJson answers for the one service that echoes a body back rather than acting on it.
public readonly record struct HandleRequest(HandleRequestKind Kind, Guid SubjectId, JsonElement Json)
{
    public bool IsJson => Json.ValueKind != JsonValueKind.Undefined;
}

// The choice a reactive service makes on every /handle body. It lives here rather than inside each
// service's entry point so it can be tested, and so the services agree on what each shape means.
public static class HandleRequestRouter
{
    private const string SubjectIdProperty = "subjectId";

    // Never raises. An empty body, and text that is not JSON at all, are Unrecognised — the same
    // answer as JSON naming no subject, so every unusable body is refused in the shared words
    // instead of failing the request and being re-driven by the reconciler.
    public static HandleRequest Classify(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return new HandleRequest(HandleRequestKind.Unrecognised, Guid.Empty, default);

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(body);
            // Cloned because the element outlives the document it was read from.
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new HandleRequest(HandleRequestKind.Unrecognised, Guid.Empty, default);
        }

        return new HandleRequest(Classify(root, out var subjectId), subjectId, root);
    }

    private static HandleRequestKind Classify(JsonElement root, out Guid subjectId)
    {
        subjectId = Guid.Empty;

        if (DagNodeService.IsNodeEnvelope(root))
            return HandleRequestKind.NodeEnvelope;

        return TryReadSubjectId(root, out subjectId)
            ? HandleRequestKind.RelationshipSubject
            : HandleRequestKind.Unrecognised;
    }

    private static bool TryReadSubjectId(JsonElement root, out Guid subjectId)
    {
        subjectId = Guid.Empty;

        if (root.ValueKind != JsonValueKind.Object)
            return false;

        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, SubjectIdProperty, StringComparison.OrdinalIgnoreCase))
                continue;

            // TryGetGuid throws rather than returning false unless the value is a string, so a body
            // carrying a number or null here would fail the request instead of being refused.
            if (property.Value.ValueKind == JsonValueKind.String && property.Value.TryGetGuid(out var candidate))
            {
                subjectId = candidate;
                return true;
            }
        }

        return false;
    }

    public static string DescribeExpectedShapes(string serviceName) =>
        $"{serviceName} expects a node envelope (runId, nodeId) or a graph relationship (subjectId).";

    public static string DescribeExpectedNodeEnvelope(string serviceName) =>
        $"{serviceName} is a DAG node; expected a node envelope (runId, nodeId).";
}
