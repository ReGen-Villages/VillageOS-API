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
            .With("assesses", DeclaredVocabularyReader.HazardTypePredicateFlag)
            .WithArchetype("HazardLevel", DeclaredVocabularyReader.HazardLevelArchetypeFlag)
            .With("reportedAs", DeclaredVocabularyReader.ReportedLevelPredicateFlag)
            .With("Earth", DeclaredVocabularyReader.RootPlaceFlag)
            .With("isIn", DeclaredVocabularyReader.PlaceNestingPredicateFlag);

        foreach (var category in WillowBend.AllocationCategoryNames)
            model.Relate(category, "is", "AllocationCategory");
        foreach (var source in WillowBend.BoundarySourceNames)
            model.Relate(source, "is", "BoundarySource");
        foreach (var hazardType in WillowBend.HazardTypeNames)
            model.Relate(hazardType, "is", "HazardType");
        foreach (var level in WillowBend.HazardLevelNames)
            model.Relate(level, "is", "HazardLevel");

        model.WithArchetype(
            "WaterDemandComponent", DeclaredVocabularyReader.WaterDemandComponentArchetypeFlag);
        foreach (var (demand, servingOrder) in WillowBend.WaterDemandNames)
            model.ArchetypeStating(demand, (DeclaredVocabularyReader.ServingOrderProperty, servingOrder))
                .Relate(demand, "is", "WaterDemandComponent");

        // A deployment's model also says what a map may draw on, which a form reads beside the categories.
        model
            .WithArchetype(FormOptionsReader.BasemapSourceArchetypeName)
            .Stating(
                WillowBend.VectorBasemapName,
                ("attribution", WillowBend.BasemapAttribution),
                ("styleUrl", WillowBend.VectorBasemapStyleUrl))
            .Relate(WillowBend.VectorBasemapName, "is", FormOptionsReader.BasemapSourceArchetypeName);

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

    /// <summary>A Thing carrying values of its own, for what a reader takes off properties rather than off
    /// the edges under an archetype.</summary>
    public DeclaredModel Stating(string name, params (string Property, object Value)[] values) =>
        Stating(name, isArchetype: false, values);

    /// <summary>An archetype carrying values of its own — a water demand states where in the queue it
    /// stands, and members of it are minted per study rather than declared here.</summary>
    public DeclaredModel ArchetypeStating(string name, params (string Property, object Value)[] values) =>
        Stating(name, isArchetype: true, values);

    private DeclaredModel Stating(
        string name, bool isArchetype, (string Property, object Value)[] values)
    {
        var id = Id(name);
        _things.RemoveAll(thing => thing.Id == id);
        _things.Add(Thing(id, name, isArchetype,
            values.ToDictionary(stated => stated.Property, stated => Stated(stated.Value))));
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

    private static SnapshotProperty Stated(object value) =>
        new(JsonSerializer.SerializeToElement(value), null, null);
}
