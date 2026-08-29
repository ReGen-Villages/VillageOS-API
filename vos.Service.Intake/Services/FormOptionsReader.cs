using System.Text.Json;
using vos.Service.Intake.Models;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Services;

/// <summary>
/// What a public form needs to draw itself, read out of the model the page cannot read for itself.
/// </summary>
/// <remarks>
/// The categories are found by the mark their archetype carries, as every vocabulary this service reads
/// is. The predicate they are asserted through is required even though a form never writes an edge: a
/// model missing it refuses every submission naming a category, and a form offering choices its own
/// service would then refuse is worse than no form.
/// </remarks>
public static class FormOptionsReader
{
    /// <summary>The archetype a basemap source is declared under. A name where the rest of this service
    /// reads a mark, because the name is the contract <c>docs/TRELLIS.md</c> §22 already states for every
    /// client that draws a map — a second way of finding the same Things would be one more thing a model's
    /// author has to know.</summary>
    public const string BasemapSourceArchetypeName = "BasemapSource";

    public const string AttributionProperty = "attribution";
    public const string StyleUrlProperty = "styleUrl";
    public const string TileUrlProperty = "tileUrl";
    public const string MaximumZoomProperty = "maximumZoom";

    public static SubscriptionSelector Selector() => new()
    {
        Names = [SubmissionFragmentComposer.IsPredicateName],
        MarkedTypes =
        [
            DeclaredVocabularyReader.AllocationCategoryArchetypeFlag,
            DeclaredVocabularyReader.HazardTypeArchetypeFlag,
            DeclaredVocabularyReader.HazardLevelArchetypeFlag,
        ],
        MarkedArchetypes =
        [
            DeclaredVocabularyReader.AllocationCategoryPredicateFlag,
            DeclaredVocabularyReader.HazardTypePredicateFlag,
            DeclaredVocabularyReader.ReportedLevelPredicateFlag,
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
        DeclaredVocabularyReader.HazardLevelNamesOrNone(snapshot));

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

    private static DeclaredBasemapSource Source(SnapshotThing thing) => new(
        thing.Id,
        thing.Name ?? string.Empty,
        Text(thing, AttributionProperty),
        Text(thing, StyleUrlProperty),
        Text(thing, TileUrlProperty),
        Number(thing, MaximumZoomProperty));

    private static string? Text(SnapshotThing thing, string property) =>
        thing.StatedValue(property) is { } stated && stated.Value.ValueKind == JsonValueKind.String
            ? stated.Value.GetString()
            : null;

    private static double? Number(SnapshotThing thing, string property) =>
        thing.StatedValue(property) is { } stated && stated.Value.ValueKind == JsonValueKind.Number
            ? stated.Value.GetDouble()
            : null;
}
