using vos.Service.Intake.Helpers;
using vos.Service.Intake.Models;

namespace vos.Service.Intake.Services;

/// <summary>The single path from what a wizard collected to what the model holds.</summary>
public sealed class SubmissionIntakeService(IntakeMyceliumClient mycelium)
{
    public async Task<ComposedSubmission> SubmitAsync(string document, CancellationToken cancellation)
    {
        var submission = SubmissionReader.Read(document);
        var predicates = new ResolvedPredicates(
            await ResolvePredicateAsync(SubmissionFragmentComposer.StudiesPredicateName, cancellation),
            await ResolvePredicateAsync(SubmissionFragmentComposer.HasPredicateName, cancellation),
            await ResolvePredicateAsync(SubmissionFragmentComposer.IsPredicateName, cancellation));

        var archetypes = new ResolvedArchetypes(
            await RequireArchetypeAsync(SubmissionFragmentComposer.SiteArchetypeName, cancellation),
            await RequireArchetypeAsync(SubmissionFragmentComposer.SiteStudyArchetypeName, cancellation),
            await RequireArchetypeAsync(SubmissionFragmentComposer.ParcelArchetypeName, cancellation),
            await RequireArchetypeAsync(SubmissionFragmentComposer.ProjectArchetypeName, cancellation),
            await RequireArchetypeAsync(SubmissionFragmentComposer.ContactArchetypeName, cancellation));

        var composed = SubmissionFragmentComposer.Compose(submission, predicates, archetypes);
        await mycelium.ApplyFragmentAsync(composed.Fragment, cancellation);
        return composed;
    }

    // An archetype is never minted here. A model missing one was not seeded from the analysis templates,
    // and a submission that quietly built untyped Things in it would leave nothing able to tell a parcel
    // from a hazard — which is the whole reason the Things point at archetypes at all.
    private async Task<Guid> RequireArchetypeAsync(string name, CancellationToken cancellation) =>
        await mycelium.FindThingIdByNameAsync(name, cancellation)
        ?? throw new ModelNotSeededError(
            $"this model holds no '{name}' archetype, so a submission has nothing to relate its Things to. "
            + "Seed the model from the analysis templates before submitting into it.");

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
