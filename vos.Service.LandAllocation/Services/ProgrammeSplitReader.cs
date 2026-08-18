using vos.Service.Shared.Subscriptions;

namespace vos.Service.LandAllocation.Services;

/// <summary>What the split reads as: the study's site and parcel, each allocation with the category it
/// names, and the allocations to follow for changes. An allocation naming no category the model holds is
/// carried as a gap rather than dropped — a split silently short by one describes a different parcel.</summary>
public sealed record ProgrammeSplit(
    double ParcelAreaHectares,
    IReadOnlyList<AllocatedCategory> Categories,
    IReadOnlyDictionary<string, Guid> AllocationIdByCategory,
    IReadOnlyList<Guid> ReadsFrom,
    IReadOnlyList<string> Uncategorised);

// Reads the programme split from one scoped snapshot: study → site → its allocations and its parcel, and
// from each allocation the category Thing it names.
//
// Every predicate is followed by the flag the model marks it with, never by a name compiled in here
// (#6551) — which is the whole reason the category is a Thing rather than a word. A category's footprint
// membership is then read off its own properties, because a flag is inherited through the `is` chain and
// a resolving reader would answer with every member of anything carrying it.
public static class ProgrammeSplitReader
{
    // `has` is the platform's own structural predicate and is named, as every reader names it. `studies`
    // is the model's, and is named only because nothing marks it yet — the model declares no flag for it,
    // and inventing one here would put the vocabulary back in a service. The category, which the model
    // does mark, is followed by that mark.
    public const string StudiesPredicate = "studies";
    public const string HasPredicate = "has";
    public const string CategoryFlag = "__IsAllocationCategoryPredicate";

    public const string BuiltFootprintFlag = "__IsBuiltFootprintCategory";
    public const string ProductiveFootprintFlag = "__IsProductiveFootprintCategory";

    public const string SharePctProperty = "sharePct";
    public const string ParcelAreaProperty = "measuredAreaHectares";

    /// <summary>The properties that invalidate a computed split. Derived from what <see cref="Read"/>
    /// actually reads, so the filter cannot come to disagree with the inputs.</summary>
    public static readonly IReadOnlySet<string> InputProperties =
        new HashSet<string>([SharePctProperty, ParcelAreaProperty], StringComparer.Ordinal);

    public static SubscriptionSelector SelectorFor(Guid studyId) => new()
    {
        Ids = [studyId],
        Names = [StudiesPredicate, HasPredicate],
        Traverse =
        [
            // Incoming: the edge runs study -> site, and the study is the seed.
            new TraverseRule { Predicate = StudiesPredicate },
            // The site holds its parcel and its allocations.
            new TraverseRule { Predicate = HasPredicate },
            // Each allocation names its category.
            new TraverseRule { PredicateFlag = CategoryFlag },
        ],
        IncludeRelationships = true,
    };

    public static ProgrammeSplit Read(SnapshotDocument snapshot, Guid studyId)
    {
        var thingsById = snapshot.Things.ToDictionary(thing => thing.Id);
        var byFlag = PredicatesCarrying(snapshot);

        var namesById = thingsById.ToDictionary(entry => entry.Key, entry => entry.Value.Name ?? string.Empty);
        var siteId = TargetNamed(snapshot, namesById, StudiesPredicate, studyId);
        if (siteId is null) return Empty();

        var held = snapshot.Relationships
            .Where(edge => edge.SubjectId == siteId && IsNamed(namesById, edge.PredicateId, HasPredicate))
            .Select(edge => edge.TargetId)
            .ToList();

        var categories = new List<AllocatedCategory>();
        var allocationIdByCategory = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var readsFrom = new List<Guid>();
        var uncategorised = new List<string>();
        double parcelArea = 0;

        foreach (var id in held)
        {
            if (!thingsById.TryGetValue(id, out var thing)) continue;

            if (Number(thing, ParcelAreaProperty) is { } area)
            {
                parcelArea = area;
                readsFrom.Add(id);
                continue;
            }

            if (Number(thing, SharePctProperty) is not { } share) continue;
            readsFrom.Add(id);

            var categoryId = TargetOf(snapshot, byFlag, CategoryFlag, id);
            if (categoryId is null || !thingsById.TryGetValue(categoryId.Value, out var category))
            {
                uncategorised.Add(thing.Name ?? id.ToString());
                continue;
            }

            var name = category.Name ?? categoryId.Value.ToString();
            categories.Add(new AllocatedCategory(name, share,
                Marked(category, BuiltFootprintFlag), Marked(category, ProductiveFootprintFlag)));
            allocationIdByCategory[name] = id;
        }

        return new ProgrammeSplit(parcelArea, categories, allocationIdByCategory, readsFrom, uncategorised);

        ProgrammeSplit Empty() => new(0, [], new Dictionary<string, Guid>(), [], []);
    }

    private static Guid? TargetOf(
        SnapshotDocument snapshot, IReadOnlyDictionary<string, HashSet<Guid>> byFlag, string flag, Guid subjectId)
    {
        foreach (var edge in snapshot.Relationships)
            if (edge.SubjectId == subjectId && Carries(byFlag, flag, edge.PredicateId))
                return edge.TargetId;

        return null;
    }

    private static Guid? TargetNamed(
        SnapshotDocument snapshot, IReadOnlyDictionary<Guid, string> namesById, string predicate, Guid subjectId)
    {
        foreach (var edge in snapshot.Relationships)
            if (edge.SubjectId == subjectId && IsNamed(namesById, edge.PredicateId, predicate))
                return edge.TargetId;

        return null;
    }

    private static bool IsNamed(IReadOnlyDictionary<Guid, string> namesById, Guid predicateId, string name) =>
        namesById.TryGetValue(predicateId, out var predicate)
        && string.Equals(predicate, name, StringComparison.OrdinalIgnoreCase);

    private static bool Carries(
        IReadOnlyDictionary<string, HashSet<Guid>> byFlag, string flag, Guid predicateId) =>
        byFlag.TryGetValue(flag, out var marked) && marked.Contains(predicateId);

    // The snapshot carries the predicate Things because the traversal named their flags, so the marks are
    // resolved here once rather than per edge.
    private static Dictionary<string, HashSet<Guid>> PredicatesCarrying(SnapshotDocument snapshot)
    {
        var byFlag = new Dictionary<string, HashSet<Guid>>(StringComparer.Ordinal) { [CategoryFlag] = [] };

        foreach (var thing in snapshot.Things)
            foreach (var (flag, marked) in byFlag)
                if (Marked(thing, flag)) marked.Add(thing.Id);

        return byFlag;
    }

    private static bool Marked(SnapshotThing thing, string flag) =>
        thing.Properties.TryGetValue(flag, out var property)
        && property.Value.ValueKind == System.Text.Json.JsonValueKind.True;

    private static double? Number(SnapshotThing thing, string name)
    {
        if (!thing.Properties.TryGetValue(name, out var property)) return null;
        return property.Value.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Number => property.Value.GetDouble(),
            System.Text.Json.JsonValueKind.String when double.TryParse(
                property.Value.GetString(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }
}
