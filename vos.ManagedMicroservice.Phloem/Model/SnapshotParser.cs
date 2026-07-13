using System.Text.Json;

namespace vos.ManagedMicroservice.Phloem.Model;

// Builds a PipelineGraph from a Mycelium subscription snapshot
// ({ snapshot: { things:[...], relationships:[...] } }). Property/edge values arrive wrapped as
// { value|Value, type }; this unwraps them to the bare value.
public static class SnapshotParser
{
    public static PipelineGraph Parse(JsonElement root)
    {
        var snapshot = root.TryGetProperty("snapshot", out var s) ? s : root;

        var things = new Dictionary<Guid, GraphThing>();
        if (snapshot.TryGetProperty("things", out var thingsArr) && thingsArr.ValueKind == JsonValueKind.Array)
            foreach (var t in thingsArr.EnumerateArray())
            {
                if (!TryGuid(t, "id", out var id)) continue;
                things[id] = new GraphThing
                {
                    Id = id,
                    Name = t.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty,
                    Properties = ParseProperties(t),
                };
            }

        var relationships = new List<GraphRelationship>();
        if (snapshot.TryGetProperty("relationships", out var relArr) && relArr.ValueKind == JsonValueKind.Array)
            foreach (var r in relArr.EnumerateArray())
            {
                if (!TryGuid(r, "id", out var id) || !TryGuid(r, "subjectId", out var subj)
                    || !TryGuid(r, "predicateId", out var pred) || !TryGuid(r, "targetId", out var tgt))
                    continue;
                relationships.Add(new GraphRelationship
                {
                    Id = id,
                    SubjectId = subj,
                    PredicateId = pred,
                    TargetId = tgt,
                    Properties = ParseProperties(r),
                });
            }

        return new PipelineGraph(things, relationships);
    }

    private static IReadOnlyDictionary<string, JsonElement> ParseProperties(JsonElement owner)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (owner.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
            foreach (var p in props.EnumerateObject())
                result[p.Name] = Unwrap(p.Value);
        return result;
    }

    // A property value is { value|Value: x, type: ... } — return x; otherwise the raw element.
    private static JsonElement Unwrap(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var field in value.EnumerateObject())
                if (string.Equals(field.Name, "value", StringComparison.OrdinalIgnoreCase))
                    return field.Value.Clone();
        return value.Clone();
    }

    private static bool TryGuid(JsonElement obj, string name, out Guid value)
    {
        value = Guid.Empty;
        return obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out value);
    }
}
