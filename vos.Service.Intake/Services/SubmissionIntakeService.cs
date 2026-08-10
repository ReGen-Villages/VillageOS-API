using vos.Service.Intake.Helpers;
using vos.Service.Intake.Models;

namespace vos.Service.Intake.Services;

/// <summary>Reads a submission, composes the fragment it becomes, and applies it — the single path from what
/// a wizard collected to what the model holds.</summary>
public sealed class SubmissionIntakeService(IntakeMyceliumClient mycelium)
{
    public async Task<ComposedSubmission> SubmitAsync(string document, CancellationToken cancellation)
    {
        var submission = SubmissionReader.Read(document);
        var predicates = new ResolvedPredicates(
            await ResolvePredicateAsync(SubmissionFragmentComposer.StudiesPredicateName, cancellation),
            await ResolvePredicateAsync(SubmissionFragmentComposer.HasPredicateName, cancellation));

        var composed = SubmissionFragmentComposer.Compose(submission, predicates);
        await mycelium.ApplyFragmentAsync(composed.Fragment, cancellation);
        return composed;
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
