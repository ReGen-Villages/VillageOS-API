using System.Text.Json;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Phloem.Model;

// Builds a PipelineGraph from a Mycelium subscription answer, wrapped ({ snapshot: {...} }) or bare.
//
// Read through the shared snapshot records rather than by walking the JSON: a value written for a name
// the Thing's archetype declares sits in the Thing's override store, not among its own properties, and
// SnapshotValues is where that rule lives. Own properties first, then the override sets, never up the
// `is` chain — a mark stays where it is stated, so an archetype's members do not answer with its mark.
public static class SnapshotParser
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static PipelineGraph Parse(JsonElement root)
    {
        var document = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("snapshot", out var wrapped)
            ? wrapped.Deserialize<SnapshotDocument>(Json)
            : root.Deserialize<SnapshotDocument>(Json);
        return Parse(document ?? new SnapshotDocument(0, [], []));
    }

    public static PipelineGraph Parse(SnapshotDocument snapshot)
    {
        var things = new Dictionary<Guid, GraphThing>();
        foreach (var thing in snapshot.Things ?? [])
        {
            if (thing.Id == Guid.Empty) continue;
            things[thing.Id] = new GraphThing
            {
                Id = thing.Id,
                Name = thing.Name ?? string.Empty,
                Properties = Stated(thing.ValuesStated()),
            };
        }

        var relationships = new List<GraphRelationship>();
        foreach (var relationship in snapshot.Relationships ?? [])
        {
            // One short of its four identifiers is dropped rather than failing the load.
            if (relationship.Id == Guid.Empty || relationship.SubjectId == Guid.Empty
                || relationship.PredicateId == Guid.Empty || relationship.TargetId == Guid.Empty)
                continue;
            relationships.Add(new GraphRelationship
            {
                Id = relationship.Id,
                SubjectId = relationship.SubjectId,
                PredicateId = relationship.PredicateId,
                TargetId = relationship.TargetId,
                Properties = Stated(relationship.ValuesStated()),
            });
        }

        return new PipelineGraph(things, relationships);
    }

    private static IReadOnlyDictionary<string, JsonElement> Stated(IEnumerable<KeyValuePair<string, SnapshotProperty>> values)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, property) in values)
            result[name] = property.Value;
        return result;
    }
}
