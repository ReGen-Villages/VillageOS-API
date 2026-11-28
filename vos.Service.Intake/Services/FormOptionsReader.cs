using System.Text.Json;
using vos.Service.Intake.Models;
using vos.Service.Shared;
using vos.Service.Shared.Geometry;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Services;

// What a public form needs to draw itself, read out of the model the page cannot read for itself.
//
// The categories are found through the role that binds their archetype, as every vocabulary this service
// reads is. The predicate they are asserted through is required even though a form never writes a relationship: a
// model missing it refuses every submission naming a category, and a form offering choices its own
// service would then refuse is worse than no form.
public static class FormOptionsReader
{
    public const string AttributionProperty = "attribution";
    public const string StyleUrlProperty = "styleUrl";
    public const string TileUrlProperty = "tileUrl";
    public const string MaximumZoomProperty = "maximumZoom";
    public const string TerrainTileUrlProperty = "terrainTileUrl";
    public const string TerrainEncodingProperty = "terrainEncoding";
    public const string TerrainExaggerationProperty = "terrainExaggeration";
    public const string BuildingSourceLayerProperty = "buildingSourceLayer";

    // The archetype whose members a report's sections name as their theme, through the role the land-intake
    // template binds it with.
    public static readonly Role ThemeRole = LandIntakeRoles.Theme;

    public const string ColourProperty = "colour";
    public const string IconProperty = "icon";
    public const string OrderProperty = "order";

    private static readonly Role[] TermArchetypeRoles =
    [
        LandIntakeRoles.AllocationCategory, LandIntakeRoles.HazardType, LandIntakeRoles.HazardLevel, ThemeRole,
        LandIntakeRoles.SiteLayerKind,
    ];

    private static readonly Role[] PredicateRoles =
    [
        LandIntakeRoles.CategorizedAs, LandIntakeRoles.Assesses, LandIntakeRoles.ReportedLevel,
        LandIntakeRoles.ShownUnder,
    ];

    public static SubscriptionSelector Selector() => new()
    {
        Ids = RoleBindings.Identifiers(
            [.. TermArchetypeRoles, .. PredicateRoles, .. PositionLookupReader.Roles, LandIntakeRoles.BasemapSource,
                .. AreaMethodReader.Roles, .. LandIntakeRoles.BoundarySourceRoles]),
        Names = [PlatformPredicates.Is, RoleBindings.BindsPredicateName],
        BoundTypes = RoleBindings.Identifiers([.. TermArchetypeRoles, LandIntakeRoles.BasemapSource]),
        BoundArchetypes = RoleBindings.Identifiers(PositionLookupReader.Roles),
        Traverse = [RoleBindings.FollowBindings, PositionLookupReader.FollowFallbacks],
        IncludeRelationships = true,
    };

    public static FormOptions Read(SnapshotDocument snapshot)
    {
        var categories = DeclaredVocabularyReader.AllocationCategories(snapshot).Terms;
        // A form asking somebody what they have seen has to offer the words the model holds, for the same
        // reason it offers the allocation categories: a term typed freehand is refused on submission, and
        // the person who typed it is the last to find out.
        var hazardTypes = DeclaredVocabularyReader.HazardTypesOrNone(snapshot);
        var hazardLevels = DeclaredVocabularyReader.HazardLevelsOrNone(snapshot);
        var layerKinds = snapshot.Bound(LandIntakeRoles.SiteLayerKind).FirstOrDefault() is { } layerKind
            ? DeclaredVocabularyReader.TermsUnder(snapshot, layerKind.Id)
            : [];
        var themes = ThemeThings(snapshot);

        return new FormOptions(
            categories,
            BasemapSources(snapshot),
            hazardTypes,
            hazardLevels,
            DefaultProgramme(snapshot),
            PositionLookupReader.ParcelLookups(snapshot).Count > 0,
            PositionLookupReader.PlaceSearches(snapshot).Count > 0,
            [.. themes.Select(Theme)],
            LayerKinds(snapshot, layerKinds, themes),
            BoundarySources(snapshot),
            Wording(snapshot, [
                .. categories, .. hazardTypes, .. hazardLevels, .. layerKinds,
                .. themes.Select(theme => new DeclaredTerm(theme.Name ?? string.Empty, theme.Id)),
            ]),
            DeclaredVocabularyReader.AreaMethod(snapshot).RoleWord);
    }

    private static List<DeclaredLayerKind> LayerKinds(
        SnapshotDocument snapshot, IReadOnlyList<DeclaredTerm> kinds, IReadOnlyList<SnapshotThing> themes)
    {
        var thingsById = snapshot.Things.ToDictionary(thing => thing.Id);
        var shownUnder = snapshot.Bound(LandIntakeRoles.ShownUnder).FirstOrDefault();
        var kindAndTheme = shownUnder is null
            ? []
            : snapshot.Relationships
                .Where(edge => edge.PredicateId == shownUnder.Id)
                .Select(edge => (edge.SubjectId, edge.TargetId))
                .ToHashSet();
        return
        [
            .. kinds.Where(kind => thingsById.ContainsKey(kind.Id))
                .Select(kind => new DeclaredLayerKind(
                    kind.Id,
                    kind.Name,
                    thingsById[kind.Id].StatedText(ColourProperty),
                    [
                        .. themes.Where(theme => kindAndTheme.Contains((kind.Id, theme.Id)))
                            .Select(theme => theme.Name ?? string.Empty),
                    ])),
        ];
    }

    // What each offered term is called in each language, as the term Thing states it: a JSON object from
    // language code to words. A term is submitted by its identifier; the words are only shown.
    public const string WordingProperty = "wording";

    private static Dictionary<string, IReadOnlyDictionary<string, string>> Wording(
        SnapshotDocument snapshot, IReadOnlyList<DeclaredTerm> terms)
    {
        var thingsById = snapshot.Things.ToDictionary(thing => thing.Id);
        var wording = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        foreach (var term in terms)
        {
            if (!thingsById.TryGetValue(term.Id, out var thing)) continue;
            if (WordsByLanguage(thing.StatedText(WordingProperty)) is { Count: > 0 } words)
                wording.TryAdd(term.Name, words);
        }
        return wording;
    }

    private static Dictionary<string, string>? WordsByLanguage(string? stated)
    {
        if (stated is null) return null;
        try
        {
            using var document = JsonDocument.Parse(stated);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            return document.RootElement.EnumerateObject()
                .Where(entry => entry.Value.ValueKind == JsonValueKind.String)
                .ToDictionary(entry => entry.Name, entry => entry.Value.GetString()!);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The starting split, from the share each category Thing states for itself. Only the
    // categories stating one are in it, so a model declaring no defaults offers a page that starts
    // where the wizard starts — with nothing chosen.
    public const string DefaultShareProperty = "defaultSharePercent";

    private static List<DeclaredBoundarySource> BoundarySources(SnapshotDocument snapshot) =>
    [
        .. LandIntakeRoles.BoundarySourceRoles.SelectMany(role => snapshot.Bound(role)
            .Select(source => new DeclaredBoundarySource(role.Id, source.Id, source.Name ?? string.Empty))),
    ];

    private static List<DeclaredShare> DefaultProgramme(SnapshotDocument snapshot)
    {
        var thingsById = snapshot.Things.ToDictionary(thing => thing.Id);
        return
        [
            .. DeclaredVocabularyReader.AllocationCategories(snapshot).Terms
                .Where(term => thingsById.ContainsKey(term.Id))
                .Select(term => (term.Id, Share: thingsById[term.Id].StatedNumber(DefaultShareProperty)))
                .Where(category => category.Share is not null)
                .Select(category => new DeclaredShare(category.Id, category.Share!.Value)),
        ];
    }

    // A model declaring no imagery is a form with no map rather than a service that cannot answer: the
    // basemap is a deployment's own choice, and a submission carrying coordinates typed in by hand is a
    // whole one.
    private static List<DeclaredBasemapSource> BasemapSources(SnapshotDocument snapshot)
    {
        var archetype = snapshot.Bound(LandIntakeRoles.BasemapSource).SingleOrDefault();
        if (archetype is null) return [];

        var thingsById = snapshot.Things.ToDictionary(thing => thing.Id);
        return
        [
            .. DeclaredVocabularyReader.TermsUnder(snapshot, archetype.Id)
                .Where(declared => thingsById.ContainsKey(declared.Id))
                .Select(declared => Source(thingsById[declared.Id])),
        ];
    }

    // A model seeded before the report had tiles binds no theme archetype, and its report draws as a
    // list. A role bound to two Things would leave nothing able to say which vocabulary a section's
    // theme belongs to, so that model is refused as the categories' would be.
    private static List<SnapshotThing> ThemeThings(SnapshotDocument snapshot)
    {
        var bound = snapshot.Bound(ThemeRole);
        if (bound.Count == 0) return [];
        if (bound.Count > 1)
            throw new ModelNotSeededError(
                $"this model binds the role {ThemeRole.Word} to more than one Thing — "
                + string.Join(", ", bound.Select(thing => $"'{thing.Name}'"))
                + " — so nothing can say which of them a section's theme is declared under.");

        var thingsById = snapshot.Things.ToDictionary(thing => thing.Id);
        return
        [
            .. DeclaredVocabularyReader.TermsUnder(snapshot, bound[0].Id)
                .Where(declared => thingsById.ContainsKey(declared.Id))
                .Select(declared => thingsById[declared.Id])
                // Ordered tiles first, as the model orders them; the rest keep the name order the terms
                // arrive in, so a page draws the same grid on every read.
                .OrderBy(theme => theme.StatedWholeNumber(OrderProperty) ?? long.MaxValue),
        ];
    }

    private static DeclaredTheme Theme(SnapshotThing thing) => new(
        thing.Name ?? string.Empty,
        thing.StatedText(ColourProperty),
        thing.StatedText(IconProperty),
        thing.StatedWholeNumber(OrderProperty));

    private static DeclaredBasemapSource Source(SnapshotThing thing) => new(
        thing.Id,
        thing.Name ?? string.Empty,
        thing.StatedText(AttributionProperty),
        thing.StatedText(StyleUrlProperty),
        thing.StatedText(TileUrlProperty),
        thing.StatedNumber(MaximumZoomProperty),
        thing.StatedWholeNumber(OrderProperty),
        thing.StatedText(TerrainTileUrlProperty),
        thing.StatedText(TerrainEncodingProperty),
        thing.StatedNumber(TerrainExaggerationProperty),
        thing.StatedText(BuildingSourceLayerProperty));
}
