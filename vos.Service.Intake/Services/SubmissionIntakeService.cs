using vos.Service.Intake.Helpers;
using vos.Service.Intake.Models;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Services;

/// <summary>The single path from what a wizard collected to what the model holds.</summary>
public sealed class SubmissionIntakeService(
    IntakeMyceliumClient mycelium, ISubscriptionClient subscriptions, ILogger<SubmissionIntakeService> logger,
    TimeProvider time)
{
    // Every archetype a submission points its Things at. Order matters only to the refusal, which reports
    // missing names in it.
    private static readonly string[] ArchetypeNames =
    [
        SubmissionFragmentComposer.SiteArchetypeName,
        SubmissionFragmentComposer.SiteStudyArchetypeName,
        SubmissionFragmentComposer.ParcelArchetypeName,
        SubmissionFragmentComposer.ProjectArchetypeName,
        SubmissionFragmentComposer.ContactArchetypeName,
        SubmissionFragmentComposer.ProgrammeAllocationArchetypeName,
        SubmissionFragmentComposer.HazardAssessmentArchetypeName,
        SubmissionFragmentComposer.DataSourceArchetypeName,
        SubmissionFragmentComposer.SubmissionArchetypeName,
    ];

    public async Task<ComposedSubmission> SubmitAsync(Submission submission, CancellationToken cancellation)
    {
        // None of these lookups reads another's answer, and a fragment upserts so a wizard posts the whole
        // submission again on every save. Awaited one after another they would spend a round trip each on
        // every keystroke's worth of progress, and one more again for every archetype added later.
        var studies = ResolvePredicateAsync(SubmissionFragmentComposer.StudiesPredicateName, cancellation);
        var has = ResolvePredicateAsync(SubmissionFragmentComposer.HasPredicateName, cancellation);
        var isEdge = ResolvePredicateAsync(SubmissionFragmentComposer.IsPredicateName, cancellation);
        var proposes = ResolvePredicateAsync(SubmissionFragmentComposer.ProposesPredicateName, cancellation);
        var arrival = ArrivalTimeAsync(submission.SubmissionId, cancellation);
        var archetypeLookups = ArchetypeNames
            .Select(name => mycelium.FindThingIdByNameAsync(name, cancellation))
            .ToArray();

        await Task.WhenAll([studies, has, isEdge, proposes, arrival, .. archetypeLookups.Cast<Task>()]);

        // Kept against the name rather than the position it was asked in, so the two orderings cannot drift
        // apart and hand a submission the Parcel archetype where it asked for the Site.
        var found = ArchetypeNames
            .Zip(archetypeLookups, (name, lookup) => (name, identifier: lookup.Result))
            .ToDictionary(pair => pair.name, pair => pair.identifier);
        RefuseAModelMissingAnyArchetype(found);

        var predicates = new ResolvedPredicates(await studies, await has, await isEdge, await proposes);
        var archetypes = new ResolvedArchetypes(
            found[SubmissionFragmentComposer.SiteArchetypeName]!.Value,
            found[SubmissionFragmentComposer.SiteStudyArchetypeName]!.Value,
            found[SubmissionFragmentComposer.ParcelArchetypeName]!.Value,
            found[SubmissionFragmentComposer.ProjectArchetypeName]!.Value,
            found[SubmissionFragmentComposer.ContactArchetypeName]!.Value,
            found[SubmissionFragmentComposer.ProgrammeAllocationArchetypeName]!.Value,
            found[SubmissionFragmentComposer.HazardAssessmentArchetypeName]!.Value,
            found[SubmissionFragmentComposer.DataSourceArchetypeName]!.Value,
            found[SubmissionFragmentComposer.SubmissionArchetypeName]!.Value);

        // After the archetype gate rather than beside the lookups above. Both refuse an unseeded model, and
        // run together the one that answered first would decide which of the two refusals the planner saw.
        var vocabulary = await ReadDeclaredVocabularyAsync(cancellation);

        var composed = SubmissionFragmentComposer.Compose(
            submission, predicates, archetypes, vocabulary, await arrival);
        await mycelium.ApplyFragmentAsync(composed.Fragment, cancellation);
        return composed;
    }

    // When this submission arrived, which is only the arrival the first time. A wizard saves as the planner
    // fills the form in and a fragment upserts, so a time written on every save would record the last save.
    //
    // A submission carrying no identifier is refused when it is composed, and asking about a record whose
    // identifier cannot be derived would refuse it here instead — with a message about a lookup rather than
    // about the field that is missing.
    private async Task<DateTime?> ArrivalTimeAsync(string? submissionId, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(submissionId))
            return time.GetUtcNow().UtcDateTime;

        var record = StableIdentity.Derive(submissionId, SubmissionFragmentComposer.SubmissionRole);
        return await mycelium.HoldsThingAsync(record, cancellation)
            ? null
            : time.GetUtcNow().UtcDateTime;
    }

    // The vocabularies a submitted word is resolved against, read from the model on every submission: a
    // term added to the model has to reach the next submission, and a copy held here would be the list in
    // code this read exists to remove.
    private Task<DeclaredVocabulary> ReadDeclaredVocabularyAsync(CancellationToken cancellation) =>
        subscriptions.ReadAsync(
            DeclaredVocabularyReader.Selector(), DeclaredVocabularyReader.Read, logger, cancellation);

    // An archetype is never minted here. A model missing one was not seeded from the analysis templates,
    // and a submission that quietly built untyped Things in it would leave nothing able to tell a parcel
    // from a hazard — which is the whole reason the Things point at archetypes at all.
    //
    // Every missing name is reported rather than whichever lookup happened to answer first: an unseeded
    // model is missing all of them, and naming one per attempt would take an attempt per archetype to
    // describe.
    private static void RefuseAModelMissingAnyArchetype(IReadOnlyDictionary<string, Guid?> found)
    {
        var missing = ArchetypeNames.Where(name => found[name] is null).ToArray();
        if (missing.Length == 0)
            return;

        throw new ModelNotSeededError(
            $"this model holds no {string.Join(", ", missing.Select(name => $"'{name}'"))} "
            + "archetype, so a submission has nothing to relate its Things to. "
            + "Seed the model from the analysis templates before submitting into it.");
    }

    // A predicate the model already holds is used as it stands. One it does not gets an identifier derived
    // from its name rather than a fresh one, so two submissions into the same model relate their Things
    // through one predicate instead of each building its own.
    private async Task<PredicateIdentity> ResolvePredicateAsync(string name, CancellationToken cancellation)
    {
        var known = await mycelium.FindThingIdByNameAsync(name, cancellation);
        return known is { } identifier
            ? new PredicateIdentity(name, identifier, Minted: false)
            : new PredicateIdentity(name, StableIdentity.DerivePredicate(name), Minted: true);
    }
}
