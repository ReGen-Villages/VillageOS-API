using System.Text.Json;
using vos.Service.Intake.Helpers;
using vos.Service.Intake.Models;

namespace vos.Service.Intake.Services;

/// <summary>
/// What the platform worked out about one submitter's land, answered to the submitter and to nobody else.
/// </summary>
/// <remarks>
/// Two things have to hold before anything is read. The reference is known to whoever submitted and to
/// anybody who guessed one, so on its own it names a submission without establishing whose it is; the
/// ticket says somebody answered a code sent to a particular mailbox, and says nothing about which
/// submission. Together they are the whole claim: this mailbox, and the submission that names it.
/// <para>
/// A reference nothing was submitted under and a reference naming a different address are refused in one
/// wording. Told apart, answering would say whether a reference exists for anybody who tried one.
/// </para>
/// </remarks>
public sealed class SubmissionFindingsService(
    IntakeMyceliumClient mycelium, ILogger<SubmissionFindingsService> logger)
{
    /// <summary>What a caller is told when the reference names no submission, names one under another
    /// address, or names one that has been cleared. One wording, because telling them apart answers
    /// "was anything ever submitted under this reference" to whoever asks.</summary>
    public const string NotYourSubmission =
        "No submission was found for that reference and address. Check both, and note that a submission "
        + "is cleared once it has been dealt with.";

    /// <summary>The findings for one submission, or null where the reference and the address do not name
    /// one between them.</summary>
    public async Task<Findings?> ReadAsync(
        string submissionId, string emailAddress, CancellationToken cancellation)
    {
        var declarations = await ReadDeclarationsAsync(submissionId, emailAddress, cancellation);
        if (declarations is null) return null;

        var siteId = StableIdentity.Derive(submissionId, SiteRole);
        using var reading = await mycelium.ReadAsync(
            FindingsReader.FindingsSelector(siteId, declarations.Spec), cancellation);

        var things = FindingsReader.ThingsToAnswerWith(
            reading.RootElement, declarations.PersonalDetailArchetype);

        // The site itself, not merely something the walk reached: a reference whose submission has been
        // cleared reads the same as one nothing was ever submitted under, which is what it is.
        if (!things.Any(thing => FindingsReader.Identifier(thing) == siteId)) return null;

        var relationships = FindingsReader.RelationshipsToAnswerWith(reading.RootElement, things);
        logger.LogInformation("Findings for submission {Reference} were read", submissionId);

        return new Findings(
            declarations.Spec,
            siteId,
            [.. things.Select(thing => thing.Clone())],
            [.. relationships.Select(edge => edge.Clone())],
            await RangesAsync(FindingsReader.JudgedThings(things), cancellation));
    }

    private const string SiteRole = "site";
    private const string ContactRole = "contact";

    private async Task<SubmitterDeclarations?> ReadDeclarationsAsync(
        string submissionId, string emailAddress, CancellationToken cancellation)
    {
        var contactId = StableIdentity.Derive(submissionId, ContactRole);
        using var declared = await mycelium.ReadAsync(
            FindingsReader.DeclarationSelector(contactId), cancellation);

        var declarations = FindingsReader.ReadDeclarations(declared.RootElement, contactId);

        // The ticket proved a mailbox and the reference named a submission; this is where the two meet.
        // Compared under the same normalisation a code was answered under, so somebody who verified one
        // spelling of their address and submitted another is not turned away with nothing to correct.
        return declarations.ContactAddress is { } named
               && string.Equals(
                   AddressVerification.Key(named), AddressVerification.Key(emailAddress), StringComparison.Ordinal)
            ? declarations
            : null;
    }

    private async Task<Dictionary<string, JsonElement>> RangesAsync(
        List<Guid> judged, CancellationToken cancellation)
    {
        // One read each, run together: they do not read each other's answers, and a page waiting on them
        // one after another would wait a round trip per balance the model judges.
        var reads = judged.Select(id => mycelium.ReadRangesAsync(id, cancellation)).ToArray();
        var answered = await Task.WhenAll(reads);

        var ranges = new Dictionary<string, JsonElement>();
        foreach (var (id, document) in judged.Zip(answered))
        {
            if (document is null) continue;
            using (document) ranges[id.ToString()] = document.RootElement.Clone();
        }
        return ranges;
    }
}
