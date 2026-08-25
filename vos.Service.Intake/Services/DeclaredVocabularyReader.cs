using System.Text.Json;
using vos.Service.Intake.Models;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Services;

/// <summary>
/// The vocabularies a submitted word is resolved against, read from the model that declares them: what a
/// programme allocation is for, how a parcel boundary was obtained, and what a hazard assessment is about.
/// </summary>
/// <remarks>
/// Nothing here names an archetype or a predicate. Each vocabulary is found by the mark its archetype
/// carries and each edge by the mark its predicate carries, so a model that renamed either keeps
/// answering — and a model that declares a term this service has never heard of resolves it anyway,
/// which is the whole reason the terms are Things.
/// <para>
/// The alternative fails quietly rather than loudly: selecting by name, a renamed archetype answers with
/// an empty vocabulary, and every submission is then refused for naming a term the model was still
/// holding.
/// </para>
/// </remarks>
public static class DeclaredVocabularyReader
{
    public const string AllocationCategoryArchetypeFlag = "__IsAllocationCategoryArchetype";
    public const string AllocationCategoryPredicateFlag = "__IsAllocationCategoryPredicate";
    public const string BoundarySourceArchetypeFlag = "__IsBoundarySourceArchetype";
    public const string BoundarySourcePredicateFlag = "__IsBoundarySourcePredicate";
    public const string HazardTypeArchetypeFlag = "__IsHazardTypeArchetype";
    public const string HazardTypePredicateFlag = "__IsHazardTypePredicate";

    /// <summary>Every vocabulary in one read. The terms come from the archetype marks and the predicates
    /// from their own, and `is` is named so the walk from an archetype to its terms can recognise the
    /// edges the snapshot carries.</summary>
    public static SubscriptionSelector Selector() => new()
    {
        Names = [SubmissionFragmentComposer.IsPredicateName],
        MarkedTypes = [AllocationCategoryArchetypeFlag, BoundarySourceArchetypeFlag, HazardTypeArchetypeFlag],
        MarkedArchetypes =
            [AllocationCategoryPredicateFlag, BoundarySourcePredicateFlag, HazardTypePredicateFlag],
        IncludeRelationships = true,
    };

    public static DeclaredVocabulary Read(SnapshotDocument snapshot) => new(
        TermsMarked(snapshot, AllocationCategoryArchetypeFlag, AllocationCategoryPredicateFlag,
            "what a programme allocation is for"),
        TermsMarked(snapshot, BoundarySourceArchetypeFlag, BoundarySourcePredicateFlag,
            "how a parcel boundary was obtained"),
        TermsMarked(snapshot, HazardTypeArchetypeFlag, HazardTypePredicateFlag,
            "what a hazard assessment is about"));

    private static DeclaredTerms TermsMarked(
        SnapshotDocument snapshot, string archetypeFlag, string predicateFlag, string whatItDeclares)
    {
        var archetype = OneCarrying(snapshot, archetypeFlag, whatItDeclares);
        if (!archetype.IsArchetype)
            throw new ModelNotSeededError(
                $"the Thing carrying '{archetypeFlag}' is not a type, so nothing can `is` it and "
                + $"{whatItDeclares} has no terms under it.");

        var predicate = OneCarrying(snapshot, predicateFlag, whatItDeclares);
        return new DeclaredTerms(
            new DeclaredTerm(predicate.Name ?? string.Empty, predicate.Id),
            TermsUnder(snapshot, archetype.Id));
    }

    // A model holding two carriers of one mark leaves nothing able to say which vocabulary a submitted word
    // belongs to, and picking either would resolve the same word two ways on two submissions.
    private static SnapshotThing OneCarrying(SnapshotDocument snapshot, string flag, string whatItDeclares)
    {
        var carrying = snapshot.Things.Where(thing => Marked(thing, flag)).ToList();
        return carrying.Count switch
        {
            1 => carrying[0],
            0 => throw new ModelNotSeededError(
                $"this model declares no '{flag}', so {whatItDeclares} is a word here and cannot be resolved. "
                + "Seed the model from the analysis templates before submitting into it."),
            _ => throw new ModelNotSeededError(
                $"this model carries '{flag}' on more than one Thing — "
                + string.Join(", ", carrying.Select(thing => $"'{thing.Name}'"))
                + $" — so nothing can say which of them {whatItDeclares} is declared under."),
        };
    }

    // Every Thing that `is` the archetype, directly or through an intermediate type. Walked outwards from
    // the archetype rather than upwards from each Thing, so the snapshot's relationships are read once per
    // level instead of once per Thing. An archetype is not one of its own terms.
    private static List<DeclaredTerm> TermsUnder(SnapshotDocument snapshot, Guid archetypeId)
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

    // Own properties only. A mark is inherited through the `is` chain, so a resolving reader would answer
    // with every term as well as the archetype, and there would be no archetype left to select.
    private static bool Marked(SnapshotThing thing, string flag) =>
        thing.Properties.TryGetValue(flag, out var property)
        && property.Value.ValueKind == JsonValueKind.True;
}
