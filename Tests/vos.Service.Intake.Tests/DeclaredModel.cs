using System.Text.Json;
using vos.Service.Intake.Services;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Tests;

/// <summary>A model as the scoped read returns it: Things, their own properties, and the edges between
/// them. Built rather than fetched, so a test can say what a model declares and what it leaves out.</summary>
public sealed class DeclaredModel
{
    private readonly Dictionary<string, Guid> _idsByName = new(StringComparer.Ordinal);
    private readonly List<SnapshotThing> _things = [];
    private readonly List<SnapshotRelationship> _edges = [];

    /// <summary>A model seeded from the land-intake template: every vocabulary, each under a marked
    /// archetype and reached through a marked predicate.</summary>
    public static DeclaredModel Seeded()
    {
        var model = new DeclaredModel()
            .WithArchetype("AllocationCategory", DeclaredVocabularyReader.AllocationCategoryArchetypeFlag)
            .With("categorizedAs", DeclaredVocabularyReader.AllocationCategoryPredicateFlag)
            .WithArchetype("BoundarySource", DeclaredVocabularyReader.BoundarySourceArchetypeFlag)
            .With("obtainedBy", DeclaredVocabularyReader.BoundarySourcePredicateFlag)
            .WithArchetype("HazardType", DeclaredVocabularyReader.HazardTypeArchetypeFlag)
            .With("assesses", DeclaredVocabularyReader.HazardTypePredicateFlag);

        foreach (var category in WillowBend.AllocationCategoryNames)
            model.Relate(category, "is", "AllocationCategory");
        foreach (var source in WillowBend.BoundarySourceNames)
            model.Relate(source, "is", "BoundarySource");
        foreach (var hazardType in WillowBend.HazardTypeNames)
            model.Relate(hazardType, "is", "HazardType");

        return model;
    }

    public Guid Id(string name)
    {
        if (_idsByName.TryGetValue(name, out var existing)) return existing;
        var id = Guid.NewGuid();
        _idsByName[name] = id;
        _things.Add(Thing(id, name, isArchetype: false, []));
        return id;
    }

    public DeclaredModel With(string name, params string[] flags) => Carrying(name, isArchetype: false, flags);

    public DeclaredModel WithArchetype(string name, params string[] flags) =>
        Carrying(name, isArchetype: true, flags);

    public DeclaredModel Relate(string subject, string predicate, string target)
    {
        _edges.Add(new SnapshotRelationship(
            Guid.NewGuid(), null, Id(subject), Id(predicate), Id(target),
            [], new Dictionary<string, InheritedPropertySet>(), []));
        return this;
    }

    public DeclaredModel Without(string name)
    {
        _things.RemoveAll(thing => thing.Name == name);
        return this;
    }

    public SnapshotDocument Build() => new(0, _things, _edges);

    private DeclaredModel Carrying(string name, bool isArchetype, IEnumerable<string> flags)
    {
        var id = Id(name);
        _things.RemoveAll(thing => thing.Id == id);
        _things.Add(Thing(id, name, isArchetype, flags.ToDictionary(flag => flag, _ => True)));
        return this;
    }

    private static SnapshotThing Thing(
        Guid id, string name, bool isArchetype, Dictionary<string, SnapshotProperty> properties) =>
        new(id, name, isArchetype, properties, new Dictionary<string, InheritedPropertySet>(), [], []);

    private static SnapshotProperty True =>
        new(JsonDocument.Parse("true").RootElement, "vos.Boolean", null);
}
