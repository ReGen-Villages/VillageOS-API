using System.Text.Json;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Services;

// What the model declares a shared file as, found by the marks rather than by name: the
// archetype a document `is`, the predicate a project shares it through, and `is` itself.
public sealed record SharedDocumentDeclarations(Guid Archetype, string ArchetypeName, Guid Predicate, string PredicateName, Guid Is);

public sealed record SharedDocumentListing(
    Guid Id, string FileName, string? Description, string? ContentType, long? SizeBytes, string? SharedAt);

public static class SharedDocumentReader
{
    public const string ArchetypeFlag = "__IsSharedDocumentArchetype";
    public const string PredicateFlag = "__IsSharedDocumentPredicate";
    public const string FileNameProperty = "fileName";
    public const string DescriptionProperty = "description";
    public const string ContentTypeProperty = "contentType";
    public const string SizeBytesProperty = "sizeBytes";
    public const string StoredAsProperty = "storedAs";
    public const string SharedAtProperty = "sharedAt";

    public static SubscriptionSelector DeclarationSelector() => new()
    {
        Names = [SubmissionFragmentComposer.IsPredicateName],
        MarkedArchetypes = [ArchetypeFlag, PredicateFlag],
    };

    // Null where the model declares no shared file: a submitter is then told files are not
    // taken here, rather than a file being kept that nothing can list.
    public static SharedDocumentDeclarations? ReadDeclarations(SnapshotDocument snapshot)
    {
        var archetype = snapshot.Things.FirstOrDefault(thing => thing.CarriesFlag(ArchetypeFlag));
        var predicate = snapshot.Things.FirstOrDefault(thing => thing.CarriesFlag(PredicateFlag));
        var isPredicate = snapshot.Things.FirstOrDefault(thing =>
            string.Equals(thing.Name, SubmissionFragmentComposer.IsPredicateName, StringComparison.Ordinal));
        if (archetype is null || predicate is null || isPredicate is null) return null;
        return new SharedDocumentDeclarations(
            archetype.Id, archetype.Name ?? string.Empty, predicate.Id, predicate.Name ?? string.Empty, isPredicate.Id);
    }

    // The documents a project shares, read off the walk from it: the Things at the far end of
    // the marked predicate, with what each states.
    public static SubscriptionSelector ListingSelector(Guid projectId, string predicateName) => new()
    {
        Ids = [projectId],
        Traverse = [new TraverseRule { Predicate = predicateName, Direction = "outgoing" }],
        IncludeRelationships = true,
    };

    public static IReadOnlyList<SharedDocumentListing> Listing(SnapshotDocument snapshot, Guid projectId, Guid predicateId)
    {
        var thingsById = snapshot.Things.ToDictionary(thing => thing.Id);
        return [.. snapshot.Relationships
            .Where(edge => edge.SubjectId == projectId && edge.PredicateId == predicateId)
            .Select(edge => thingsById.GetValueOrDefault(edge.TargetId))
            .OfType<SnapshotThing>()
            .Select(document => new SharedDocumentListing(
                document.Id,
                Text(document, FileNameProperty) ?? document.Name ?? string.Empty,
                Text(document, DescriptionProperty),
                Text(document, ContentTypeProperty),
                Number(document, SizeBytesProperty),
                Text(document, SharedAtProperty)))
            .OrderBy(document => document.SharedAt, StringComparer.Ordinal)];
    }

    private static string? Text(SnapshotThing thing, string property) =>
        thing.StatedValue(property) is { } stated && stated.Value.ValueKind == JsonValueKind.String
            ? stated.Value.GetString()
            : null;

    private static long? Number(SnapshotThing thing, string property) =>
        thing.StatedValue(property) is { } stated && stated.Value.ValueKind == JsonValueKind.Number && stated.Value.TryGetInt64(out var number)
            ? number
            : null;
}
