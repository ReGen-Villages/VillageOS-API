using System.Text.Json;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Forage.Tests.Helpers;

// One place to build a snapshot, so a test reads as the shape it is asserting rather than as plumbing.
// Shared by every test over what a run reads out of the model.
internal sealed class ModelBuilder
{
    // The archetype a coverage's values are stored under, standing for the one the model declares.
    private const string StatedOver = "SourceCoverage";

    private readonly Dictionary<string, Guid> _ids = new(StringComparer.Ordinal);
    private readonly List<SnapshotThing> _things = new();
    private readonly List<SnapshotRelationship> _edges = new();

    internal static SnapshotThing Thing(Guid id, string? name) => new(
        id, name, false,
        new Dictionary<string, SnapshotProperty>(),
        null,
        Array.Empty<string>(),
        Array.Empty<Guid>());

    internal static SnapshotRelationship Edge(Guid subject, Guid predicate, Guid target) => new(
        Guid.NewGuid(), null, subject, predicate, target,
        new Dictionary<string, SnapshotProperty>(),
        null,
        Array.Empty<string>());

    public Guid Id(string name)
    {
        if (_ids.TryGetValue(name, out var existing)) return existing;
        var id = Guid.NewGuid();
        _ids[name] = id;
        _things.Add(Thing(id, name));
        return id;
    }

    public ModelBuilder Relate(string subject, string predicate, string target)
    {
        _edges.Add(Edge(Id(subject), Id(predicate), Id(target)));
        return this;
    }

    // Re-declares a Thing as a type, carrying the flag when it is one a reader finds by mark.
    public ModelBuilder Archetype(string name, string? flag = null)
    {
        var id = Id(name);
        var properties = flag == null
            ? new Dictionary<string, SnapshotProperty>()
            : new Dictionary<string, SnapshotProperty>
            {
                [flag] = new(JsonDocument.Parse("true").RootElement, null, null),
            };

        Replace(id, name, isArchetype: true, properties);
        return this;
    }

    // What a run writes onto a coverage as it goes. Every such name is one the coverage's archetype
    // declares, so the model stores the value as an override under that archetype rather than among the
    // coverage's own properties, and this builds the shape a reader actually meets (#6805).
    public ModelBuilder Carrying(string name, string property, object value)
    {
        var id = Id(name);
        var existing = _things.Single(thing => thing.Id == id);
        var stated = existing.InheritedOverrides?.GetValueOrDefault(StatedOver)?.Properties
                     ?? new Dictionary<string, SnapshotProperty>();
        var properties = new Dictionary<string, SnapshotProperty>(stated)
        {
            [property] = new(JsonDocument.Parse(JsonSerializer.Serialize(value)).RootElement, null, null),
        };

        Replace(id, name, existing.IsArchetype, existing.Properties,
            new Dictionary<string, InheritedPropertySet>
            {
                [StatedOver] = new(StatedOver, properties, null),
            });
        return this;
    }

    private void Replace(
        Guid id, string name, bool isArchetype, Dictionary<string, SnapshotProperty> properties,
        Dictionary<string, InheritedPropertySet>? overrides = null)
    {
        _things.RemoveAll(thing => thing.Id == id);
        _things.Add(new SnapshotThing(
            id, name, isArchetype, properties, overrides, Array.Empty<string>(), Array.Empty<Guid>()));
    }

    public SnapshotDocument Build() => new(0, _things, _edges);
}
