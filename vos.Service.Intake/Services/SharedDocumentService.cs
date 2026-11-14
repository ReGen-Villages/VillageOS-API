using System.Globalization;
using vos.Service.Intake.Helpers;
using vos.Service.Intake.Models;
using vos.Service.Shared.Subscriptions;
using vos.Service.Shared;

namespace vos.Service.Intake.Services;

public enum ShareOutcome { Shared, NotYourSubmission, NotTakenHere, Refused }

public sealed record SharedFile(
    ShareOutcome Outcome, string? Address = null, SharedDocumentListing? Document = null, Refusal? Refusal = null);

public enum RemovalOutcome { Removed, NotYourSubmission, NotShared }

public sealed record RemovedFile(RemovalOutcome Outcome, string? Address = null);

// A file a submitter shares about their land: the bytes go to the store beside this service, and the
// model gets the Thing it declares for one — its name, description, media type and size — related to
// the submission's project through the predicate the model marks, so the review page can list it and
// the retention pass takes it with a rejected submission.
public sealed class SharedDocumentService(
    IntakeMyceliumClient mycelium,
    HeldDeclarations declarations,
    DocumentStore store,
    ModelClock time,
    ILogger<SharedDocumentService> logger)
{
    public const long MaximumFileBytes = 25 * 1024 * 1024;
    // The file and the form around it: a description and the multipart framing.
    public const long MaximumRequestBytes = MaximumFileBytes + 64 * 1024;

    // Every refusal of a layer file is found before anything is kept, so a refused file leaves neither bytes nor
    // Things behind.
    public async Task<SharedFile> ShareAsync(
        string submissionId, Func<string, bool> ticketIssuedFor, string fileName, string contentType,
        string? description, Func<Stream> openFile, IReadOnlyDictionary<string, string>? chosenKinds,
        CancellationToken cancellation)
    {
        var address = await AddressOfAsync(submissionId, cancellation);
        if (address is null || !ticketIssuedFor(address)) return new SharedFile(ShareOutcome.NotYourSubmission);

        var declared = await declarations.ReadAsync(
            SharedDocumentReader.DeclarationSelector(), SharedDocumentReader.ReadDeclarations, cancellation);
        if (declared is null) return new SharedFile(ShareOutcome.NotTakenHere);

        var layer = chosenKinds is null ? null : await LayerDeclarationsAsync(cancellation);
        IReadOnlyList<(ChosenFeature Chosen, DeclaredTerm Kind)> features;
        try
        {
            features = layer is null ? [] : FeaturesOf(fileName, openFile, chosenKinds!, layer);
        }
        catch (SubmissionError refused)
        {
            return new SharedFile(ShareOutcome.Refused, Refusal: refused.AsRefusal());
        }

        await using var bytes = openFile();
        var stored = await store.SaveAsync(submissionId, fileName, bytes, cancellation);
        var sharedAt = time.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        var documentId = StableIdentity.Derive(submissionId, $"document:{stored.StoredAs}");
        var project = StableIdentity.Derive(submissionId, "project");

        List<FragmentThing> things =
        [
            new(documentId, fileName, new Dictionary<string, TypedValue>
            {
                [SharedDocumentReader.FileNameProperty] = new() { TypeInfo = "vos.String", Value = fileName },
                [SharedDocumentReader.DescriptionProperty] = new() { TypeInfo = "vos.String", Value = description ?? string.Empty },
                [SharedDocumentReader.ContentTypeProperty] = new() { TypeInfo = "vos.String", Value = contentType },
                [SharedDocumentReader.SizeBytesProperty] = new() { TypeInfo = "vos.LongInteger", Value = stored.SizeBytes },
                [SharedDocumentReader.StoredAsProperty] = new() { TypeInfo = "vos.String", Value = stored.StoredAs },
                [SharedDocumentReader.SharedAtProperty] = new() { TypeInfo = "vos.DateTime", Value = sharedAt },
            }),
        ];
        List<FragmentRelationship> relationships =
        [
            new($"{fileName} is {declared.ArchetypeName}", documentId, declared.Is, declared.Archetype),
            new($"project {declared.PredicateName} {fileName}", project, declared.Predicate, documentId),
        ];

        var site = StableIdentity.Derive(submissionId, SubmissionFragmentComposer.SiteRole);
        foreach (var ((chosen, kind), position) in features.Select((feature, position) => (feature, position)))
        {
            var featureId = StableIdentity.Derive(submissionId, $"document:{stored.StoredAs}:feature:{position}");
            things.Add(new FragmentThing(featureId, chosen.Name, new Dictionary<string, TypedValue>
            {
                [SiteLayerReader.GeometryProperty] = TypedValue.Written(VosTypeNames.GeoJson, chosen.Geometry),
            }));
            relationships.Add(new($"{chosen.Name} is {layer!.Feature.Name}", featureId, declared.Is, layer.Feature.Id));
            relationships.Add(new($"site {layer.Has.Name} {chosen.Name}", site, layer.Has.Id, featureId));
            relationships.Add(new($"{chosen.Name} {layer.Kinds.Predicate.Name} {kind.Name}", featureId, layer.Kinds.Predicate.Id, kind.Id));
            relationships.Add(new($"{chosen.Name} {layer.SourcedFrom.Name} {fileName}", featureId, layer.SourcedFrom.Id, documentId));
        }

        await mycelium.ApplyFragmentAsync(new ModelFragment("shared document", things, relationships), cancellation);

        logger.LogInformation("Submission {Reference} shared a file of {Bytes} bytes with {Features} layer features",
            submissionId, stored.SizeBytes, features.Count);
        return new SharedFile(ShareOutcome.Shared, address,
            new SharedDocumentListing(documentId, fileName, description, contentType, stored.SizeBytes, sharedAt));
    }

    private static List<(ChosenFeature Chosen, DeclaredTerm Kind)> FeaturesOf(
        string fileName, Func<Stream> openFile, IReadOnlyDictionary<string, string> chosenKinds,
        SiteLayerDeclarations layer)
    {
        using var bytes = openFile();
        var chosen = LayerFeatureChoice.Choose(LayerFileReader.Read(fileName, bytes), chosenKinds);
        return [.. chosen.Select(feature =>
            (feature, SubmissionFragmentComposer.Resolve(layer.Kinds, feature.Name, feature.Kind)))];
    }

    private async Task<SiteLayerDeclarations> LayerDeclarationsAsync(CancellationToken cancellation) =>
        await declarations.ReadAsync(SiteLayerReader.Selector(), SiteLayerReader.Read, cancellation)
        ?? throw new ModelNotSeededError(
            "this model declares no site layer, so a layer file has no feature to store. Seed the model from "
            + "the land-intake template before sharing one.");

    // The features go first and the bytes last, so a removal that fails part way leaves the document listed and
    // can be asked again.
    public async Task<RemovedFile> RemoveAsync(
        string submissionId, Func<string, bool> ticketIssuedFor, Guid documentId, CancellationToken cancellation)
    {
        var address = await AddressOfAsync(submissionId, cancellation);
        if (address is null || !ticketIssuedFor(address)) return new RemovedFile(RemovalOutcome.NotYourSubmission);

        var declared = await declarations.ReadAsync(
            SharedDocumentReader.DeclarationSelector(), SharedDocumentReader.ReadDeclarations, cancellation);
        if (declared is null) return new RemovedFile(RemovalOutcome.NotShared);

        var project = StableIdentity.Derive(submissionId, "project");
        var listing = await mycelium.ReadSnapshotAsync(
            SharedDocumentReader.ListingSelector(project, declared.PredicateName), cancellation);
        if (SharedDocumentReader.Shared(listing, project, declared.Predicate, documentId) is not { } document)
            return new RemovedFile(RemovalOutcome.NotShared);

        var layer = await declarations.ReadAsync(SiteLayerReader.Selector(), SiteLayerReader.Read, cancellation);
        IReadOnlyList<Guid> features = layer is null
            ? []
            : SiteLayerReader.FeaturesFrom(
                await mycelium.ReadSnapshotAsync(SiteLayerReader.FeaturesSelector(documentId), cancellation),
                documentId, layer, declared.Is);
        foreach (var feature in features)
            await mycelium.DeleteThingAsync(feature, cancellation);
        await mycelium.DeleteThingAsync(documentId, cancellation);
        if (SharedDocumentReader.StoredAs(document) is { } storedAs)
            store.Remove(submissionId, storedAs);

        logger.LogInformation("Submission {Reference} removed a file and its {Features} layer features",
            submissionId, features.Count);
        return new RemovedFile(RemovalOutcome.Removed, address);
    }

    // The files a submission holds, or null where the ticket was issued for no address the
    // submission names.
    public async Task<(IReadOnlyList<SharedDocumentListing> Documents, string Address)?> ListAsync(
        string submissionId, Func<string, bool> ticketIssuedFor, CancellationToken cancellation)
    {
        var address = await AddressOfAsync(submissionId, cancellation);
        if (address is null || !ticketIssuedFor(address)) return null;

        var declared = await declarations.ReadAsync(
            SharedDocumentReader.DeclarationSelector(), SharedDocumentReader.ReadDeclarations, cancellation);
        if (declared is null) return ([], address);

        var project = StableIdentity.Derive(submissionId, "project");
        var listing = await mycelium.ReadSnapshotAsync(
            SharedDocumentReader.ListingSelector(project, declared.PredicateName), cancellation);
        return (SharedDocumentReader.Listing(listing, project, declared.Predicate), address);
    }

    // Takes the files of every submission the model no longer holds. The retention pass prunes
    // a rejected submission's Things once its period has run; the bytes cannot go with them, since the
    // model never held them, so this asks the model which submissions still stand and forgets the rest.
    // A model that declares no shared file is asked nothing: one not seeded yet, or not the one the files
    // were taken into, would otherwise read as having let every submission go.
    public async Task<int> ReclaimAsync(CancellationToken cancellation)
    {
        var held = store.SubmissionsHeld();
        if (held.Count == 0) return 0;

        var declared = await declarations.ReadAsync(
            SharedDocumentReader.DeclarationSelector(), SharedDocumentReader.ReadDeclarations, cancellation);
        if (declared is null) return 0;

        var records = held.Select(submissionId => StableIdentity.Derive(submissionId, SubmissionFragmentComposer.SubmissionRole)).ToList();
        var standing = (await mycelium.ReadSnapshotAsync(
                new SubscriptionSelector { Ids = records, IncludeRelationships = false }, cancellation))
            .Things.Select(thing => thing.Id).ToHashSet();

        var forgotten = 0;
        foreach (var (submissionId, record) in held.Zip(records))
        {
            if (standing.Contains(record)) continue;
            store.Forget(submissionId);
            forgotten++;
            logger.LogInformation("The files of submission {Reference} were taken out with it", submissionId);
        }
        return forgotten;
    }

    private async Task<string?> AddressOfAsync(string submissionId, CancellationToken cancellation)
    {
        var contactId = StableIdentity.Derive(submissionId, "contact");
        using var declared = await mycelium.ReadAsync(FindingsReader.DeclarationSelector(contactId), cancellation);
        return FindingsReader.ReadDeclarations(declared.RootElement, contactId).ContactAddress;
    }
}
