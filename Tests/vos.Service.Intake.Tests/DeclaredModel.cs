using System.Text.Json;
using vos.Service.Intake.Services;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Tests;

// A model as the scoped read returns it: Things, their own properties, and the relationships between
// them. Built rather than fetched, so a test can say what a model declares and what it leaves out.
public sealed class DeclaredModel
{
    private readonly Dictionary<string, Guid> _idsByName = new(StringComparer.Ordinal);
    private readonly List<SnapshotThing> _things = [];
    private readonly List<SnapshotRelationship> _edges = [];

    // A model seeded from the land-intake template: every vocabulary, each under an archetype a role
    // binds and reached through a predicate a role binds.
    public static DeclaredModel Seeded()
    {
        var model = new DeclaredModel()
            .WithArchetype("AllocationCategory").Bound(LandIntakeRoles.AllocationCategory, "AllocationCategory")
            .With("categorizedAs").Bound(LandIntakeRoles.CategorizedAs, "categorizedAs")
            .WithArchetype("BoundarySource").Bound(LandIntakeRoles.BoundarySource, "BoundarySource")
            .With("obtainedBy").Bound(LandIntakeRoles.ObtainedBy, "obtainedBy")
            .WithArchetype("HazardType").Bound(LandIntakeRoles.HazardType, "HazardType")
            .With("assesses").Bound(LandIntakeRoles.Assesses, "assesses")
            .WithArchetype("HazardLevel").Bound(LandIntakeRoles.HazardLevel, "HazardLevel")
            .With("reportedAs").Bound(LandIntakeRoles.ReportedLevel, "reportedAs")
            .With("Earth").Bound(LandIntakeRoles.RootPlace, "Earth")
            .With("isIn").Bound(LandIntakeRoles.PlaceNesting, "isIn")
            .WithArchetype("SiteStudy").Bound(LandIntakeRoles.SiteStudy, "SiteStudy");

        foreach (var (category, share) in WillowBend.DefaultProgramme)
            model.Stating(category, (FormOptionsReader.DefaultShareProperty, share))
                .Relate(category, "is", "AllocationCategory");
        foreach (var source in WillowBend.BoundarySourceNames)
            model.Relate(source, "is", "BoundarySource");
        foreach (var hazardType in WillowBend.HazardTypeNames)
            model.Relate(hazardType, "is", "HazardType");
        foreach (var level in WillowBend.HazardLevelNames)
            model.Relate(level, "is", "HazardLevel");

        model.WithArchetype("WaterDemandComponent").Bound(LandIntakeRoles.WaterDemandComponent, "WaterDemandComponent");
        foreach (var (demand, servingOrder) in WillowBend.WaterDemandNames)
            model.ArchetypeStating(demand, (DeclaredVocabularyReader.ServingOrderProperty, servingOrder))
                .Relate(demand, "is", "WaterDemandComponent");

        // The two position lookups the open-data catalogue registers: standalone Things each bound by its role,
        // the way the catalogue declares them, with a reshape of their own for the caller to apply.
        model
            .Bound(PositionLookupReader.ParcelLookupRole, WillowBend.ParcelRegisterName)
            .Bound(PositionLookupReader.PlaceSearchRole, WillowBend.PlaceSearchName)
            .Stating(WillowBend.ParcelRegisterName,
                (PositionLookupReader.TransformProperty, WillowBend.ParcelRegisterTransform),
                (PositionLookupReader.AttributionProperty, WillowBend.ParcelRegisterAttribution),
                (PositionLookupReader.SouthLatitudeProperty, 41.0),
                (PositionLookupReader.NorthLatitudeProperty, 51.5),
                (PositionLookupReader.WestLongitudeProperty, -5.5),
                (PositionLookupReader.EastLongitudeProperty, 10.0))
            .Stating(WillowBend.PlaceSearchName,
                (PositionLookupReader.TransformProperty, WillowBend.PlaceSearchTransform),
                (PositionLookupReader.AttributionProperty, WillowBend.PlaceSearchAttribution));

        // A deployment's model also says what a map may draw on, which a form reads beside the categories.
        model
            .WithArchetype(FormOptionsReader.BasemapSourceArchetypeName)
            .Stating(
                WillowBend.VectorBasemapName,
                ("attribution", WillowBend.BasemapAttribution),
                ("styleUrl", WillowBend.VectorBasemapStyleUrl),
                (FormOptionsReader.TerrainTileUrlProperty, WillowBend.TerrainTileUrl),
                (FormOptionsReader.TerrainEncodingProperty, WillowBend.TerrainEncoding),
                (FormOptionsReader.TerrainExaggerationProperty, WillowBend.TerrainExaggeration),
                (FormOptionsReader.BuildingSourceLayerProperty, WillowBend.BuildingSourceLayer))
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

    // The role Thing, under the identifier every model declares it by, binding the named Thing along `binds`. Held
    // by identifier rather than by name: a predicate and the role binding it can share a word, as categorizedAs does.
    public DeclaredModel Bound(Role role, string name)
    {
        if (_things.All(thing => thing.Id != role.Id))
            _things.Add(Thing(role.Id, role.Word, isArchetype: false, []));
        _edges.Add(new SnapshotRelationship(
            Guid.NewGuid(), null, role.Id, Id(RoleBindings.BindsPredicateName), Id(name),
            [], new Dictionary<string, InheritedPropertySet>(), []));
        return this;
    }

    public DeclaredModel Unbound(Role role)
    {
        _edges.RemoveAll(edge => edge.SubjectId == role.Id);
        return this;
    }

    // A Thing carrying values of its own, for what a reader takes off properties rather than off
    // the relationships under an archetype.
    public DeclaredModel Stating(string name, params (string Property, object Value)[] values) =>
        Stating(name, isArchetype: false, values);

    // An archetype carrying values of its own — a water demand states where in the queue it
    // stands, and members of it are minted per study rather than declared here.
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
