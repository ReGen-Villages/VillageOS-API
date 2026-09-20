using System.Text.Json;
using vos.Service.Intake.Models;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Services;

// What a public form needs to draw itself, read out of the model the page cannot read for itself.
//
// The categories are found by the mark their archetype carries, as every vocabulary this service reads
// is. The predicate they are asserted through is required even though a form never writes an edge: a
// model missing it refuses every submission naming a category, and a form offering choices its own
// service would then refuse is worse than no form.
public static class FormOptionsReader
{
    // The archetype a basemap source is declared under. A name where the rest of this service
    // reads a mark, because the name is the contract the map chapter of docs/FIELD_GUIDE.md already states for every
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

    // The mark on the archetype whose members a report's sections name as their theme. Declared
    // in the platform repository's land-intake.template.json; the two agree by this literal.
    public const string ThemeArchetypeFlag = "__IsThemeArchetype";

    public const string ColourProperty = "colour";
    public const string IconProperty = "icon";
    public const string OrderProperty = "order";

    public static SubscriptionSelector Selector() => new()
    {
        Names = [SubmissionFragmentComposer.IsPredicateName],
        MarkedTypes =
        [
            DeclaredVocabularyReader.AllocationCategoryArchetypeFlag,
            DeclaredVocabularyReader.HazardTypeArchetypeFlag,
            DeclaredVocabularyReader.HazardLevelArchetypeFlag,
            ThemeArchetypeFlag,
        ],
        MarkedArchetypes =
        [
            DeclaredVocabularyReader.AllocationCategoryPredicateFlag,
            DeclaredVocabularyReader.HazardTypePredicateFlag,
            DeclaredVocabularyReader.ReportedLevelPredicateFlag,
            PositionLookupReader.ParcelLookupFlag,
            PositionLookupReader.PlaceSearchFlag,
        ],
        Types = [BasemapSourceArchetypeName],
        IncludeRelationships = true,
    };

    public static FormOptions Read(SnapshotDocument snapshot) => new(
        [.. DeclaredVocabularyReader.AllocationCategories(snapshot).Terms.Select(term => term.Name)],
        BasemapSources(snapshot),
        // A form asking somebody what they have seen has to offer the words the model holds, for the same
        // reason it offers the allocation categories: a term typed freehand is refused on submission, and
        // the person who typed it is the last to find out.
        DeclaredVocabularyReader.HazardTypeNamesOrNone(snapshot),
        DeclaredVocabularyReader.HazardLevelNamesOrNone(snapshot),
        DefaultProgramme(snapshot),
        PositionLookupReader.ParcelLookups(snapshot).Count > 0,
        PositionLookupReader.PlaceSearch(snapshot) is not null,
        Themes(snapshot));

    // The starting split, from the share each category Thing states for itself. Only the
    // categories stating one are in it, so a model declaring no defaults offers a page that starts
    // where the wizard starts — with nothing chosen.
    public const string DefaultShareProperty = "defaultSharePct";

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

    // A model seeded before the report had tiles declares no theme archetype, and its report draws as a
    // list. Two Things carrying the mark would leave nothing able to say which vocabulary a section's
    // theme belongs to, so that model is refused as the categories' would be.
    private static List<DeclaredTheme> Themes(SnapshotDocument snapshot)
    {
        var carrying = snapshot.Things.Where(thing => thing.CarriesFlag(ThemeArchetypeFlag)).ToList();
        if (carrying.Count == 0) return [];
        if (carrying.Count > 1)
            throw new ModelNotSeededError(
                $"this model carries '{ThemeArchetypeFlag}' on more than one Thing — "
                + string.Join(", ", carrying.Select(thing => $"'{thing.Name}'"))
                + " — so nothing can say which of them a section's theme is declared under.");

        var thingsById = snapshot.Things.ToDictionary(thing => thing.Id);
        return
        [
            .. DeclaredVocabularyReader.TermsUnder(snapshot, carrying[0].Id)
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
