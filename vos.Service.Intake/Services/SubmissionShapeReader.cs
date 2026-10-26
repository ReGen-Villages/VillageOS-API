using vos.Service.Intake.Models;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Services;

public sealed record SubmissionShape(
    ResolvedArchetypes Archetypes, PredicateIdentity Studies, PredicateIdentity Proposes, PredicateIdentity ServedAfter);

public static class SubmissionShapeReader
{
    private static readonly Role[] ArchetypeRoles =
    [
        LandIntakeRoles.Site, LandIntakeRoles.Parcel, LandIntakeRoles.Project, LandIntakeRoles.PersonalDetail,
        LandIntakeRoles.ProgrammeAllocation, LandIntakeRoles.HazardAssessment, LandIntakeRoles.SubmittedSource,
        LandIntakeRoles.Submission,
    ];

    private static readonly Role[] PredicateRoles =
        [LandIntakeRoles.StudiedSite, LandIntakeRoles.ProposedSite, LandIntakeRoles.ServiceOrder];

    public static SubscriptionSelector Selector() => new()
    {
        Ids = RoleBindings.Identifiers([.. ArchetypeRoles, .. PredicateRoles]),
        Names = [RoleBindings.BindsPredicateName],
        Traverse = [RoleBindings.FollowBindings],
        IncludeRelationships = true,
    };

    public static SubmissionShape Read(SnapshotDocument snapshot)
    {
        RefuseAModelBindingNothingToAnyRole(snapshot);

        DeclaredTerm Archetype(Role role)
        {
            var bound = DeclaredVocabularyReader.OneBound(snapshot, role, $"what a submission's {role.Word} is");
            return new DeclaredTerm(bound.Name ?? "", bound.Id);
        }

        PredicateIdentity Predicate(Role role)
        {
            var bound = DeclaredVocabularyReader.OneBound(snapshot, role, $"the predicate {role.Word} names");
            return new PredicateIdentity(bound.Name ?? "", bound.Id, Minted: false);
        }

        return new SubmissionShape(
            new ResolvedArchetypes(
                Archetype(LandIntakeRoles.Site), Archetype(LandIntakeRoles.Parcel), Archetype(LandIntakeRoles.Project),
                Archetype(LandIntakeRoles.PersonalDetail), Archetype(LandIntakeRoles.ProgrammeAllocation),
                Archetype(LandIntakeRoles.HazardAssessment), Archetype(LandIntakeRoles.SubmittedSource),
                Archetype(LandIntakeRoles.Submission)),
            Predicate(LandIntakeRoles.StudiedSite), Predicate(LandIntakeRoles.ProposedSite),
            Predicate(LandIntakeRoles.ServiceOrder));
    }

    // Nothing is created for a role the model leaves unbound: a model missing one was not seeded from the analysis
    // templates, and a submission that quietly built untyped Things in it could not tell a parcel from a hazard.
    // Every unbound role is named at once, because an unseeded model is missing all of them.
    private static void RefuseAModelBindingNothingToAnyRole(SnapshotDocument snapshot)
    {
        var unbound = ArchetypeRoles.Concat(PredicateRoles)
            .Where(role => snapshot.Bound(role).Count == 0)
            .Select(role => role.Word)
            .ToArray();
        if (unbound.Length == 0) return;

        throw new ModelNotSeededError(
            $"this model binds nothing to the roles {string.Join(", ", unbound)}, so a submission has nothing to "
            + "relate its Things to. Seed the model from the analysis templates before submitting into it.");
    }
}
