using System.Text.Json;
using vos.Service.Intake.Models;
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
    // The archetype a basemap source is declared under. A name where the rest of this service
    // reads a role, because the name is the contract the map chapter of docs/FIELD_GUIDE.md already states for every
    // client that draws a map — a second way of finding the same Things would be one more thing a model's
    // author has to know.
    public const string BasemapSourceArchetypeName = "BasemapSource";

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
    ];

    private static readonly Role[] PredicateRoles =
    [
        LandIntakeRoles.CategorizedAs, LandIntakeRoles.Assesses, LandIntakeRoles.ReportedLevel,
    ];

    public static SubscriptionSelector Selector() => new()
    {
        Ids = RoleBindings.Identifiers([.. TermArchetypeRoles, .. PredicateRoles]),
        Names = [SubmissionFragmentComposer.IsPredicateName, RoleBindings.BindsPredicateName],
        BoundTypes = RoleBindings.Identifiers(TermArchetypeRoles),
        MarkedArchetypes = [PositionLookupReader.ParcelLookupFlag, PositionLookupReader.PlaceSearchFlag],
        Traverse = [RoleBindings.FollowBindings],
        Types = [BasemapSourceArchetypeName],
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

        return new FormOptions(
            [.. categories.Select(term => term.Name)],
            BasemapSources(snapshot),
            [.. hazardTypes.Select(term => term.Name)],
            [.. hazardLevels.Select(term => term.Name)],
            DefaultProgramme(snapshot),
            PositionLookupReader.ParcelLookups(snapshot).Count > 0,
            PositionLookupReader.PlaceSearch(snapshot) is not null,
            Themes(snapshot),
            Wording(snapshot, [.. categories, .. hazardTypes, .. hazardLevels]));
    }

    // What each offered term is called in each language, as the term Thing states it: a JSON object from
    // language code to words. A term is still submitted by its name; the words are only shown.
    public const string WordingProperty = "wording";

    private static Dictionary<string, IReadOnlyDictionary<string, string>> Wording(
        SnapshotDocument snapshot, IReadOnlyList<DeclaredTerm> terms)
    {
        var thingsById = snapshot.Things.ToDictionary(thing => thing.Id);
        var wording = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        foreach (var term in terms)
        {
            if (!thingsById.TryGetValue(term.Id, out var thing)) continue;
            if (WordsByLanguage(Text(thing, WordingProperty)) is { Count: > 0 } words)
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

    private static List<DeclaredShare> DefaultProgramme(SnapshotDocument snapshot)
    {
        var thingsById = snapshot.Things.ToDictionary(thing => thing.Id);
        return
        [
            .. DeclaredVocabularyReader.AllocationCategories(snapshot).Terms
                .Where(term => thingsById.ContainsKey(term.Id))
                .Select(term => (term.Name, Share: Number(thingsById[term.Id], DefaultShareProperty)))
                .Where(category => category.Share is not null)
                .Select(category => new DeclaredShare(category.Name, category.Share!.Value)),
        ];
    }

    // A model declaring no imagery is a form with no map rather than a service that cannot answer: the
    // basemap is a deployment's own choice, and a submission carrying coordinates typed in by hand is a
    // whole one.
    private static List<DeclaredBasemapSource> BasemapSources(SnapshotDocument snapshot)
    {
        var archetype = snapshot.Things.SingleOrDefault(thing =>
            thing.IsArchetype
            && string.Equals(thing.Name, BasemapSourceArchetypeName, StringComparison.Ordinal));
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
    private static List<DeclaredTheme> Themes(SnapshotDocument snapshot)
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
                .Select(declared => Theme(thingsById[declared.Id]))
                // Ordered tiles first, as the model orders them; the rest keep the name order the terms
                // arrive in, so a page draws the same grid on every read.
                .OrderBy(theme => theme.Order ?? long.MaxValue),
        ];
    }

    private static DeclaredTheme Theme(SnapshotThing thing) => new(
        thing.Name ?? string.Empty,
        Text(thing, ColourProperty),
        Text(thing, IconProperty),
        WholeNumber(thing, OrderProperty));

    private static long? WholeNumber(SnapshotThing thing, string property) =>
        thing.StatedValue(property) is { } stated
        && stated.Value.ValueKind == JsonValueKind.Number && stated.Value.TryGetInt64(out var number)
            ? number
            : null;

    private static DeclaredBasemapSource Source(SnapshotThing thing) => new(
        thing.Id,
        thing.Name ?? string.Empty,
        Text(thing, AttributionProperty),
        Text(thing, StyleUrlProperty),
        Text(thing, TileUrlProperty),
        Number(thing, MaximumZoomProperty),
        Text(thing, TerrainTileUrlProperty),
        Text(thing, TerrainEncodingProperty),
        Number(thing, TerrainExaggerationProperty),
        Text(thing, BuildingSourceLayerProperty));

    private static string? Text(SnapshotThing thing, string property) =>
        thing.StatedValue(property) is { } stated && stated.Value.ValueKind == JsonValueKind.String
            ? stated.Value.GetString()
            : null;

    private static double? Number(SnapshotThing thing, string property) =>
        thing.StatedValue(property) is { } stated && stated.Value.ValueKind == JsonValueKind.Number
            ? stated.Value.GetDouble()
            : null;
}
