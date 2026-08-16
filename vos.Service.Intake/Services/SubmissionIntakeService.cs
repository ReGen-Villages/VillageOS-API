using vos.Service.Intake.Helpers;
using vos.Service.Intake.Models;

namespace vos.Service.Intake.Services;

/// <summary>The single path from what a wizard collected to what the model holds.</summary>
public sealed class SubmissionIntakeService(IntakeMyceliumClient mycelium)
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
    ];

    public async Task<ComposedSubmission> SubmitAsync(string document, CancellationToken cancellation)
    {
        var submission = SubmissionReader.Read(document);

        // None of these lookups reads another's answer, and a fragment upserts so a wizard posts the whole
        // submission again on every save. Awaited one after another they would spend a round trip each on
        // every keystroke's worth of progress, and one more again for every archetype added later.
        var studies = ResolvePredicateAsync(SubmissionFragmentComposer.StudiesPredicateName, cancellation);
        var has = ResolvePredicateAsync(SubmissionFragmentComposer.HasPredicateName, cancellation);
        var isEdge = ResolvePredicateAsync(SubmissionFragmentComposer.IsPredicateName, cancellation);
        var archetypeLookups = ArchetypeNames
            .Select(name => mycelium.FindThingIdByNameAsync(name, cancellation))
            .ToArray();

        await Task.WhenAll([studies, has, isEdge, .. archetypeLookups.Cast<Task>()]);

        // Kept against the name rather than the position it was asked in, so the two orderings cannot drift
        // apart and hand a submission the Parcel archetype where it asked for the Site.
        var found = ArchetypeNames
            .Zip(archetypeLookups, (name, lookup) => (name, identifier: lookup.Result))
            .ToDictionary(pair => pair.name, pair => pair.identifier);
        RefuseAModelMissingAnyArchetype(found);

        var predicates = new ResolvedPredicates(await studies, await has, await isEdge);
        var archetypes = new ResolvedArchetypes(
            found[SubmissionFragmentComposer.SiteArchetypeName]!.Value,
            found[SubmissionFragmentComposer.SiteStudyArchetypeName]!.Value,
            found[SubmissionFragmentComposer.ParcelArchetypeName]!.Value,
            found[SubmissionFragmentComposer.ProjectArchetypeName]!.Value,
            found[SubmissionFragmentComposer.ContactArchetypeName]!.Value,
            found[SubmissionFragmentComposer.ProgrammeAllocationArchetypeName]!.Value,
            found[SubmissionFragmentComposer.HazardAssessmentArchetypeName]!.Value,
            found[SubmissionFragmentComposer.DataSourceArchetypeName]!.Value);

        var composed = SubmissionFragmentComposer.Compose(submission, predicates, archetypes);
        await mycelium.ApplyFragmentAsync(composed.Fragment, cancellation);
        return composed;
    }

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
