using System.Text.Json;
using static vos.Service.Shared.MyceliumClientBase;

namespace vos.Service.Phloem.Model;

// Builds a PipelineGraph from a Mycelium subscription snapshot
// ({ snapshot: { things:[...], relationships:[...] } }). Property/edge values arrive wrapped as
// { value, typeInfo }; this unwraps them to the bare value.
//
// Every member is read without regard to case: the broker serialises a snapshot with no naming policy, so
// a Thing arrives as Id/Name/Properties, while the container keys around it are written in lower case.
public static class SnapshotParser
{
    public static PipelineGraph Parse(JsonElement root)
    {
        var snapshot = TryGetPropertyCaseInsensitive(root, "snapshot", out var s) ? s : root;

        var things = new Dictionary<Guid, GraphThing>();
        if (TryGetPropertyCaseInsensitive(snapshot, "things", out var thingsArr) && thingsArr.ValueKind == JsonValueKind.Array)
            foreach (var t in thingsArr.EnumerateArray())
            {
                if (!TryGuid(t, "id", out var id)) continue;
                things[id] = new GraphThing
                {
                    Id = id,
                    Name = TryGetPropertyCaseInsensitive(t, "name", out var n) ? n.GetString() ?? string.Empty : string.Empty,
                    Properties = ParseProperties(t),
                };
            }

        var relationships = new List<GraphRelationship>();
        if (TryGetPropertyCaseInsensitive(snapshot, "relationships", out var relArr) && relArr.ValueKind == JsonValueKind.Array)
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
        if (TryGetPropertyCaseInsensitive(owner, "properties", out var props) && props.ValueKind == JsonValueKind.Object)
            foreach (var p in props.EnumerateObject())
                result[p.Name] = Unwrap(p.Value);
        return result;
    }

    // A property value is { value: x, typeInfo: ... } — return x; otherwise the raw element.
    private static JsonElement Unwrap(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object && TryGetPropertyCaseInsensitive(value, "value", out var bare)
            ? bare.Clone()
            : value.Clone();

    private static bool TryGuid(JsonElement obj, string name, out Guid value)
    {
        value = Guid.Empty;
        return TryGetPropertyCaseInsensitive(obj, name, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out value);
    }
}
