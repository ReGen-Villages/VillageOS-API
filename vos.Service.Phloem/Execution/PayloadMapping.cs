using System.Text.Json;
using System.Text.Json.Nodes;

namespace vos.Service.Phloem.Execution;

// Field-level wire mapping (#5874): extract a dotted field-path from an upstream output, place it at
// a dotted field-path of a downstream input, and deep-merge several such contributions into one input value.
// Pure and side-effect free, so it is trivially unit-tested. A dotted path (e.g. user.id) navigates
// nested JSON objects; an empty path means "the whole value".
public static class PayloadMapping
{
    // The value at path within value. Empty path returns the
    // whole value. Returns null when any segment is missing (that wire then contributes nothing).
    public static JsonElement? Extract(JsonElement value, string path)
    {
        if (string.IsNullOrEmpty(path)) return value;
        var current = value;
        foreach (var segment in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
                return null;
            current = next;
        }
        return current;
    }

    // Wrap value at path, building nested objects from the
    // path segments (outermost first). Empty path returns the value unwrapped.
    public static JsonNode? Place(string path, JsonElement value)
    {
        var node = JsonSerializer.SerializeToNode(value);
        if (string.IsNullOrEmpty(path)) return node;
        var segments = path.Split('.');
        for (var i = segments.Length - 1; i >= 0; i--)
            node = new JsonObject { [segments[i]] = node };
        return node;
    }

    // Deep-merge two JSON values: object keys combine recursively; any non-object clash resolves to
    // incoming (last wire wins). Neither input is mutated.
    public static JsonNode? Merge(JsonNode? existing, JsonNode? incoming)
    {
        if (existing is JsonObject a && incoming is JsonObject b)
        {
            var result = new JsonObject();
            foreach (var pair in a)
                result[pair.Key] = pair.Value?.DeepClone();
            foreach (var pair in b)
                result[pair.Key] = result.TryGetPropertyValue(pair.Key, out var overlap)
                    ? Merge(overlap, pair.Value)
                    : pair.Value?.DeepClone();
            return result;
        }
        return incoming?.DeepClone();
    }

    // Convert a built node back to a JsonElement for the inputs map.
    public static JsonElement ToElement(JsonNode? node) =>
        node is null ? default : node.Deserialize<JsonElement>();
}
