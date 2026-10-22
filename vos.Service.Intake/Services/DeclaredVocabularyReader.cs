using System.Text.Json;
using vos.Service.Intake.Models;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Services;

// The vocabularies a submitted word is resolved against, read from the model that declares them: what a
// programme allocation is for, how a parcel boundary was obtained, and what a hazard assessment is about.
//
// Nothing here names an archetype or a predicate. Each vocabulary is found through the role Thing that binds
// its archetype and each relationship through the role that binds its predicate, so a model that renamed
// either keeps answering — and a model that declares a term this service has never heard of resolves it
// anyway, which is the whole reason the terms are Things.
//
// The alternative fails quietly rather than loudly: selecting by name, a renamed archetype answers with
// an empty vocabulary, and every submission is then refused for naming a term the model was still
// holding.
public static class DeclaredVocabularyReader
{
    // The demands on the water a site harvests are not a vocabulary a submitted word is resolved against —
    // nothing in a submission names a demand — but are read the same way and for the same reason: a project
    // adding a third demand declares it in the analysis and this service is not redeployed.
    public const string ServingOrderProperty = "servingOrder";

    // The archetypes whose members a vocabulary is; the Things under them are what a submitted word resolves to.
    private static readonly Role[] TermArchetypeRoles =
    [
        LandIntakeRoles.AllocationCategory, LandIntakeRoles.BoundarySource, LandIntakeRoles.HazardType,
        LandIntakeRoles.HazardLevel, LandIntakeRoles.WaterDemandComponent,
    ];

    // The predicates a relationship to a term is written with, the archetype a study `is`, and where a site
    // sits: the root Place and the predicate that relationship is written with. Each is one Thing whose
    // identifier a relationship is written to, asked for without any members.
    private static readonly Role[] SingleThingRoles =
    [
        LandIntakeRoles.CategorizedAs, LandIntakeRoles.ObtainedBy, LandIntakeRoles.Assesses,
        LandIntakeRoles.ReportedLevel, LandIntakeRoles.SiteStudy, LandIntakeRoles.RootPlace,
        LandIntakeRoles.PlaceNesting,
    ];

    // Every vocabulary in one read. The role Things are asked for by identifier and their bindings followed, so
    // the snapshot says which bound Thing plays which role; the term archetypes come with their members, and
    // `is` is named so the walk from an archetype to its terms can recognise the relationships the snapshot
    // carries.
    public static SubscriptionSelector Selector() => new()
    {
        Ids = RoleBindings.Identifiers([.. TermArchetypeRoles, .. SingleThingRoles]),
        Names = [SubmissionFragmentComposer.IsPredicateName, RoleBindings.BindsPredicateName],
        BoundTypes = RoleBindings.Identifiers(TermArchetypeRoles),
        Traverse = [RoleBindings.FollowBindings],
        IncludeRelationships = true,
    };

    public static DeclaredVocabulary Read(SnapshotDocument snapshot) => new(
        AllocationCategories(snapshot),
        TermsBound(snapshot, LandIntakeRoles.BoundarySource, LandIntakeRoles.ObtainedBy,
            "how a parcel boundary was obtained"),
        HazardTypes(snapshot),
        HazardLevels(snapshot),
        PlaceNesting(snapshot),
        WaterDemands(snapshot),
        Term(OneBound(snapshot, LandIntakeRoles.SiteStudy, "what a study of a site is")));

    // The demands one harvest is served over, in the order it serves them. Empty where the model
    // declares none: a study then holds no demand and every coverage reads unassessed, which is the honest
    // answer for a model that does no water analysis — unlike a submitted word naming a term nothing
    // holds, where something has to say the word means nothing.
    internal static IReadOnlyList<DeclaredDemand> WaterDemands(SnapshotDocument snapshot)
    {
        var vocabulary = snapshot.Bound(LandIntakeRoles.WaterDemandComponent).SingleOrDefault();
        if (vocabulary is null) return [];

        var isEdge = snapshot.Things
            .Where(thing => string.Equals(
                thing.Name, SubmissionFragmentComposer.IsPredicateName, StringComparison.OrdinalIgnoreCase))
            .Select(thing => thing.Id)
            .ToHashSet();
        var under = snapshot.Relationships
            .Where(edge => edge.TargetId == vocabulary.Id && isEdge.Contains(edge.PredicateId))
            .Select(edge => edge.SubjectId)
            .ToHashSet();

        return [.. snapshot.Things
            .Where(thing => thing.IsArchetype && under.Contains(thing.Id))
            .Select(thing => new DeclaredDemand(thing.Name ?? string.Empty, thing.Id, ServingOrderOf(thing)))
            .OrderBy(demand => demand.ServingOrder)
            .ThenBy(demand => demand.Name, StringComparer.Ordinal)];
    }

    // An analysis stating no order anywhere leaves every demand at nought, which serves them in the order
    // it declared them — a poor answer, and better than refusing every submission into that model.
    private static long ServingOrderOf(SnapshotThing demand) =>
        demand.StatedValue(ServingOrderProperty) is { } stated
        && stated.Value.TryGetInt64(out var order) ? order : 0;

    // What a hazard assessment is about, offered by a form so somebody marks what they have
    // seen rather than nominating hazards from memory.
    internal static DeclaredTerms HazardTypes(SnapshotDocument snapshot) =>
        TermsBound(snapshot, LandIntakeRoles.HazardType, LandIntakeRoles.Assesses, "what a hazard assessment is about");

    // How bad a hazard is, in the words the model holds — the set a submitter picks from and
    // the set the portal's own grading resolves against.
    internal static DeclaredTerms HazardLevels(SnapshotDocument snapshot) =>
        TermsBound(snapshot, LandIntakeRoles.HazardLevel, LandIntakeRoles.ReportedLevel, "how bad a hazard is");

    // The terms where the model declares that vocabulary, and none where it does not. What a form
    // asks for: a deployment declaring no hazards draws one step fewer, the way one declaring no imagery
    // draws no map — where a submission naming a term the model does not hold is still refused, because
    // there the word is already written and something has to say it means nothing.
    internal static IReadOnlyList<DeclaredTerm> HazardTypesOrNone(SnapshotDocument snapshot) =>
        TermsOrNone(() => HazardTypes(snapshot));

    internal static IReadOnlyList<DeclaredTerm> HazardLevelsOrNone(SnapshotDocument snapshot) =>
        TermsOrNone(() => HazardLevels(snapshot));

    private static IReadOnlyList<DeclaredTerm> TermsOrNone(Func<DeclaredTerms> read)
    {
        try
        {
            return read().Terms;
        }
        catch (ModelNotSeededError)
        {
            return [];
        }
    }

    // What a programme allocation is for, which is the one vocabulary a form has to offer
    // before anybody can fill it in.
    internal static DeclaredTerms AllocationCategories(SnapshotDocument snapshot) =>
        TermsBound(snapshot, LandIntakeRoles.AllocationCategory, LandIntakeRoles.CategorizedAs,
            "what a programme allocation is for");

    // No terms to walk: a submission names no Place, so this reads the two Things a relationship is written from
    // and to. The same one-binding rule applies — two roots would put a site under a different one on
    // different submissions, and the sources selected for it would differ by run.
    private static DeclaredPlace PlaceNesting(SnapshotDocument snapshot)
    {
        const string whatItDeclares = "where a site sits";
        return new DeclaredPlace(
            Term(OneBound(snapshot, LandIntakeRoles.PlaceNesting, whatItDeclares)),
            Term(OneBound(snapshot, LandIntakeRoles.RootPlace, whatItDeclares)));
    }

    private static DeclaredTerms TermsBound(
        SnapshotDocument snapshot, Role archetypeRole, Role predicateRole, string whatItDeclares)
    {
        var archetype = OneBound(snapshot, archetypeRole, whatItDeclares);
        if (!archetype.IsArchetype)
            throw new ModelNotSeededError(
                $"the Thing the role {archetypeRole.Word} binds is not a type, so nothing can `is` it and "
                + $"{whatItDeclares} has no terms under it.");

        return new DeclaredTerms(Term(OneBound(snapshot, predicateRole, whatItDeclares)), TermsUnder(snapshot, archetype.Id));
    }

    private static DeclaredTerm Term(SnapshotThing thing) => new(thing.Name ?? string.Empty, thing.Id);

    // A model binding one role to two Things leaves nothing able to say which vocabulary a submitted word
    // belongs to, and picking either would resolve the same word two ways on two submissions.
    internal static SnapshotThing OneBound(SnapshotDocument snapshot, Role role, string whatItDeclares)
    {
        var bound = snapshot.Bound(role);
        return bound.Count switch
        {
            1 => bound[0],
            0 => throw new ModelNotSeededError(
                $"this model binds nothing to the role {role.Word}, so {whatItDeclares} is a word here and cannot be "
                + "resolved. Seed the model from the analysis templates before submitting into it."),
            _ => throw new ModelNotSeededError(
                $"this model binds the role {role.Word} to more than one Thing — "
                + string.Join(", ", bound.Select(thing => $"'{thing.Name}'"))
                + $" — so nothing can say which of them {whatItDeclares} is declared under."),
        };
    }

    // Every Thing that `is` the archetype, directly or through an intermediate type. Walked outwards from
    // the archetype rather than upwards from each Thing, so the snapshot's relationships are read once per
    // level instead of once per Thing. An archetype is not one of its own terms.
    internal static List<DeclaredTerm> TermsUnder(SnapshotDocument snapshot, Guid archetypeId)
    {
        var thingsById = snapshot.Things.ToDictionary(thing => thing.Id);
        var isEdge = snapshot.Things
            .Where(thing => string.Equals(
                thing.Name, SubmissionFragmentComposer.IsPredicateName, StringComparison.OrdinalIgnoreCase))
            .Select(thing => thing.Id)
            .ToHashSet();

        var reached = new HashSet<Guid> { archetypeId };
        var frontier = new Queue<Guid>([archetypeId]);
        var terms = new List<DeclaredTerm>();

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            foreach (var edge in snapshot.Relationships)
            {
                if (edge.TargetId != current || !isEdge.Contains(edge.PredicateId)) continue;
                if (!reached.Add(edge.SubjectId)) continue;

                frontier.Enqueue(edge.SubjectId);
                if (thingsById.TryGetValue(edge.SubjectId, out var term) && !term.IsArchetype)
                    terms.Add(new DeclaredTerm(term.Name ?? string.Empty, term.Id));
            }
        }

        // Ordered by name so two submissions refused for the same unknown word are refused in the same
        // words, whatever order the snapshot happened to carry the terms in.
        terms.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
        return terms;
    }
}
