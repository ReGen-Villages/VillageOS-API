using vos.Service.Intake.Helpers;
using vos.Service.Intake.Models;
using vos.Service.Shared.Subscriptions;
using vos.Service.Shared;

namespace vos.Service.Intake.Services;

public sealed class SubmissionIntakeService(
    IntakeMyceliumClient mycelium, ISubscriptionClient subscriptions, ILogger<SubmissionIntakeService> logger,
    ModelClock time)
{
    public async Task<ComposedSubmission> SubmitAsync(Submission submission, CancellationToken cancellation)
    {
        // None of these reads uses another's answer, and a wizard posts the whole submission again on every save,
        // so awaiting them one after another would make every save wait for each in turn.
        var shape = subscriptions.ReadAsync(
            SubmissionShapeReader.Selector(), SubmissionShapeReader.Read, logger, cancellation);
        var has = ResolvePredicateAsync(SubmissionFragmentComposer.HasPredicateName, cancellation);
        var isEdge = ResolvePredicateAsync(SubmissionFragmentComposer.IsPredicateName, cancellation);
        var arrival = ArrivalTimeAsync(submission.SubmissionId, cancellation);

        await Task.WhenAll([shape, has, isEdge, arrival]);

        var read = await shape;
        var predicates = new ResolvedPredicates(read.Studies, await has, await isEdge, read.Proposes, read.ServedAfter);
        var archetypes = read.Archetypes;

        // After the archetype gate rather than beside the lookups above. Both refuse an unseeded model, and
        // run together the one that answered first would decide which of the two refusals the planner saw.
        // The study's archetype comes with it, found through its role rather than by a name.
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
