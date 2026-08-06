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

// The choice a reactive service makes on every /handle body. It lives here rather than inside each
// service's entry point so it can be tested, and so the services agree on what each shape means.
public static class HandleRequestRouter
{
    private const string SubjectIdProperty = "subjectId";

    public static HandleRequestKind Classify(JsonElement root, out Guid subjectId)
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
