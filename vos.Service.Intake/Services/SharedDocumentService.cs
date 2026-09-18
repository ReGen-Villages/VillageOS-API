using System.Globalization;
using vos.Service.Intake.Helpers;
using vos.Service.Intake.Models;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Services;

public enum ShareOutcome { Shared, NotYourSubmission, NotTakenHere }

public sealed record SharedFile(ShareOutcome Outcome, string? Address = null, SharedDocumentListing? Document = null);

/// <summary>
/// A file a submitter shares about their land: the bytes go to the store beside this service, and the
/// model gets the Thing it declares for one — its name, description, media type and size — related to
/// the submission's project through the predicate the model marks, so the review page can list it and
/// the retention pass takes it with a rejected submission.
/// </summary>
public sealed class SharedDocumentService(
    IntakeMyceliumClient mycelium,
    ISubscriptionClient subscriptions,
    DocumentStore store,
    TimeProvider time,
    ILogger<SharedDocumentService> logger)
{
    public const long MaximumFileBytes = 25 * 1024 * 1024;
    /// <summary>The file and the form around it: a description and the multipart framing.</summary>
    public const long MaximumRequestBytes = MaximumFileBytes + 64 * 1024;

    public async Task<SharedFile> ShareAsync(
        string submissionId, Func<string, bool> ticketIssuedFor, string fileName, string contentType,
        string? description, Stream bytes, CancellationToken cancellation)
    {
        var address = await AddressOfAsync(submissionId, cancellation);
        if (address is null || !ticketIssuedFor(address)) return new SharedFile(ShareOutcome.NotYourSubmission);

        var declared = await subscriptions.ReadAsync(
            SharedDocumentReader.DeclarationSelector(), SharedDocumentReader.ReadDeclarations, logger, cancellation);
        if (declared is null) return new SharedFile(ShareOutcome.NotTakenHere);

        var stored = await store.SaveAsync(submissionId, fileName, bytes, cancellation);
        var sharedAt = time.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        var documentId = StableIdentity.Derive(submissionId, $"document:{stored.StoredAs}");
        var project = StableIdentity.Derive(submissionId, "project");

        await mycelium.ApplyFragmentAsync(new ModelFragment(
            "shared document",
            [new FragmentThing(documentId, fileName, new Dictionary<string, TypedValue>
            {
                [SharedDocumentReader.FileNameProperty] = new() { TypeInfo = "vos.String", Value = fileName },
                [SharedDocumentReader.DescriptionProperty] = new() { TypeInfo = "vos.String", Value = description ?? string.Empty },
                [SharedDocumentReader.ContentTypeProperty] = new() { TypeInfo = "vos.String", Value = contentType },
                [SharedDocumentReader.SizeBytesProperty] = new() { TypeInfo = "vos.LongInteger", Value = stored.SizeBytes },
                [SharedDocumentReader.StoredAsProperty] = new() { TypeInfo = "vos.String", Value = stored.StoredAs },
                [SharedDocumentReader.SharedAtProperty] = new() { TypeInfo = "vos.DateTime", Value = sharedAt },
            })],
            [
                new FragmentRelationship($"{fileName} is {declared.ArchetypeName}", documentId, declared.Is, declared.Archetype),
                new FragmentRelationship($"project {declared.PredicateName} {fileName}", project, declared.Predicate, documentId),
            ]), cancellation);

        logger.LogInformation("Submission {Reference} shared a file of {Bytes} bytes", submissionId, stored.SizeBytes);
        return new SharedFile(ShareOutcome.Shared, address,
            new SharedDocumentListing(documentId, fileName, description, contentType, stored.SizeBytes, sharedAt));
    }

    /// <summary>The files a submission holds, or null where the ticket was issued for no address the
    /// submission names.</summary>
    public async Task<(IReadOnlyList<SharedDocumentListing> Documents, string Address)?> ListAsync(
        string submissionId, Func<string, bool> ticketIssuedFor, CancellationToken cancellation)
    {
        var address = await AddressOfAsync(submissionId, cancellation);
        if (address is null || !ticketIssuedFor(address)) return null;

        var declared = await subscriptions.ReadAsync(
            SharedDocumentReader.DeclarationSelector(), SharedDocumentReader.ReadDeclarations, logger, cancellation);
        if (declared is null) return ([], address);

        var project = StableIdentity.Derive(submissionId, "project");
        var documents = await subscriptions.ReadAsync(
            SharedDocumentReader.ListingSelector(project, declared.PredicateName),
            snapshot => SharedDocumentReader.Listing(snapshot, project, declared.Predicate), logger, cancellation);
        return (documents, address);
    }

    /// <summary>Takes the files of every submission the model no longer holds. The retention pass prunes
    /// a rejected submission's Things once its period has run; the bytes cannot go with them, since the
    /// model never held them, so this asks the model which submissions still stand and forgets the rest.</summary>
    public async Task<int> ReclaimAsync(CancellationToken cancellation)
    {
        var held = store.SubmissionsHeld();
        if (held.Count == 0) return 0;

        var records = held.Select(submissionId => StableIdentity.Derive(submissionId, SubmissionFragmentComposer.SubmissionRole)).ToList();
        var standing = await subscriptions.ReadAsync(
            new SubscriptionSelector { Ids = records, IncludeRelationships = false },
            snapshot => snapshot.Things.Select(thing => thing.Id).ToHashSet(), logger, cancellation);

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
