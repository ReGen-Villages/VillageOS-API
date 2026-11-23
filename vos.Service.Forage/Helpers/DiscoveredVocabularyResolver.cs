using vos.Service.Shared;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Forage.Helpers;

// What one fetch wrote onto one Thing, as the run learned it from the fetch response. The response is
// the one place the words can come from: an observation is accepted into a queue and applied later, so
// reading the model back straight after the call races the drainer.
public sealed record FetchedWords(Guid SubjectId, string Source, IReadOnlyDictionary<string, string> Values);

// One relationship the run should write: the subject, the predicate the shape relationship names, and the member the
// fetched word resolved to — with the members it replaces where the subject already carries this
// predicate pointing elsewhere, so a re-graded assessment ends the run carrying one level.
public sealed record PlannedVocabularyEdge(
    Guid SubjectId,
    string SubjectName,
    Guid PredicateId,
    Guid MemberId,
    string Word,
    IReadOnlyList<Guid> ReplacedMemberIds);

// A word the vocabulary does not hold, reported rather than silently dropped or silently written: the
// observation stays on the series, no relationship is written, and every name a reader needs is carried.
public sealed record UnresolvedWord(
    string Source, string SubjectName, string Property, string Word, string Vocabulary);

public sealed record VocabularyResolution(
    IReadOnlyList<PlannedVocabularyEdge> Edges, IReadOnlyList<UnresolvedWord> Unresolved);

// Resolves the words a run's fetches wrote into the relationships the model declares. Everything is read from the declaration: each vocabulary archetype
// `is` the kind the discoveredVocabulary role binds and names the property its word arrives under, and an
// archetype-level relationship names the Thing the word is written onto and the predicate the resolved
// relationship is written through. Nothing here names a vocabulary, an archetype, a predicate or a property of
// any model — a project adding a vocabulary edits its model and deploys nothing.
public static class DiscoveredVocabularyResolver
{
    public static readonly Role DiscoveredVocabularyRole = LandIntakeRoles.DiscoveredVocabulary;

    // The property name the platform's templates write and its seed validation holds declarations to.
    public const string ResolvedFromProperty = "resolvedFromProperty";


    // A subject reaches the shape relationship's archetype through `is`, possibly via intermediate types. The
    // same handful-of-levels reasoning as Place nesting: nobody has needed a deeper type chain, and an
    // unused level costs one set expansion.
    private const int TypeChainDepth = 8;

    // The subjects' type chains, the role with its binding, the kind it binds with every vocabulary and member
    // beneath it, and the relationships incident to all of them — which is what carries the shape relationships,
    // the members' `is` relationships, and the vocabulary relationships the subjects already hold.
    public static SubscriptionSelector SelectorFor(IReadOnlyCollection<Guid> subjectIds) => new()
    {
        Ids = [.. subjectIds, DiscoveredVocabularyRole.Id],
        Names = [PlatformPredicates.Is, RoleBindings.BindsPredicateName],
        BoundTypes = [DiscoveredVocabularyRole.Id],
        Traverse = [new TraverseRule { Predicate = PlatformPredicates.Is, Depth = TypeChainDepth }],
        IncludeRelationships = true,
    };

    public static VocabularyResolution Resolve(SnapshotDocument snapshot, IReadOnlyList<FetchedWords> fetched)
    {
        var thingsById = snapshot.Things.ToDictionary(thing => thing.Id);
        var namesById = thingsById.ToDictionary(entry => entry.Key, entry => entry.Value.Name ?? string.Empty);
        var declarations = Declarations(snapshot, thingsById, namesById);

        // Keyed by subject and predicate, so two fetches writing one word in one run plan one relationship and
        // the later fetch decides it, matching the series' own latest-wins reading.
        var planned = new Dictionary<(Guid Subject, Guid Predicate), PlannedVocabularyEdge>();
        var unresolved = new List<UnresolvedWord>();

        foreach (var words in fetched)
        {
            if (!thingsById.TryGetValue(words.SubjectId, out var subject)) continue;
            var reachedTypes = TypesReachedFrom(snapshot, namesById, words.SubjectId);

            foreach (var declaration in declarations)
            {
                if (!words.Values.TryGetValue(declaration.Word, out var word)) continue;

                foreach (var shape in declaration.Shapes)
                {
                    if (!reachedTypes.Contains(shape.SubjectArchetypeId)) continue;

                    if (declaration.MembersByName.TryGetValue(word, out var memberId))
                        planned[(words.SubjectId, shape.PredicateId)] = Plan(
                            snapshot, words.SubjectId, subject.Name ?? string.Empty,
                            shape.PredicateId, memberId, word);
                    else
                        unresolved.Add(new UnresolvedWord(
                            words.Source, subject.Name ?? string.Empty,
                            declaration.Word, word, declaration.Vocabulary));
                }
            }
        }

        // A relationship already pointing at the resolved member needs nothing; it is planned above so a later
        // fetch in the same run can still move it, and dropped here once the run's answer is settled.
        var edges = planned.Values
            .Where(edge => !AlreadyInPlace(snapshot, edge))
            .OrderBy(edge => edge.SubjectName, StringComparer.Ordinal)
            .ThenBy(edge => edge.Word, StringComparer.Ordinal)
            .ToList();

        return new VocabularyResolution(edges, unresolved);
    }

    private sealed record Shape(Guid SubjectArchetypeId, Guid PredicateId);

    private sealed record Declaration(
        string Vocabulary,
        string Word,
        IReadOnlyDictionary<string, Guid> MembersByName,
        IReadOnlyList<Shape> Shapes);

    // Every archetype directly of the kind the role binds, with the property its word arrives under,
    // its members by name, and the shape relationships reaching it. A member name two Things carry resolves
    // nothing — relationship order is not defined, and picking either would resolve one word two ways
    // on two runs — so the word is left to be reported as unresolved.
    private static List<Declaration> Declarations(
        SnapshotDocument snapshot,
        IReadOnlyDictionary<Guid, SnapshotThing> thingsById,
        IReadOnlyDictionary<Guid, string> namesById)
    {
        var declarations = new List<Declaration>();
        if (snapshot.Bound(DiscoveredVocabularyRole) is not [var kind]) return declarations;

        var vocabularyIds = snapshot.Relationships
            .Where(edge => edge.TargetId == kind.Id && SnapshotWalk.IsPredicate(namesById, edge.PredicateId, PlatformPredicates.Is))
            .Select(edge => edge.SubjectId)
            .ToHashSet();

        foreach (var vocabulary in snapshot.Things)
        {
            if (!vocabularyIds.Contains(vocabulary.Id)) continue;
            if (vocabulary.StatedText(ResolvedFromProperty) is not { } word) continue;

            var members = new Dictionary<string, Guid>(StringComparer.Ordinal);
            var contested = new HashSet<string>(StringComparer.Ordinal);
            foreach (var memberId in SnapshotWalk.ThingsUnder(snapshot, namesById, [vocabulary.Id]))
            {
                if (!thingsById.TryGetValue(memberId, out var member) || member.IsArchetype) continue;
                var name = member.Name ?? string.Empty;
                if (contested.Contains(name)) continue;
                if (!members.TryAdd(name, memberId))
                {
                    members.Remove(name);
                    contested.Add(name);
                }
            }

            var shapes = snapshot.Relationships
                .Where(edge => edge.TargetId == vocabulary.Id
                               && !SnapshotWalk.IsPredicate(namesById, edge.PredicateId, PlatformPredicates.Is))
                .Select(edge => new Shape(edge.SubjectId, edge.PredicateId))
                .ToList();

            declarations.Add(new Declaration(vocabulary.Name ?? string.Empty, word, members, shapes));
        }

        return declarations;
    }

    private static PlannedVocabularyEdge Plan(
        SnapshotDocument snapshot, Guid subjectId, string subjectName,
        Guid predicateId, Guid memberId, string word)
    {
        var replaced = snapshot.Relationships
            .Where(edge => edge.SubjectId == subjectId && edge.PredicateId == predicateId
                           && edge.TargetId != memberId)
            .Select(edge => edge.TargetId)
            .ToList();

        return new PlannedVocabularyEdge(subjectId, subjectName, predicateId, memberId, word, replaced);
    }

    private static bool AlreadyInPlace(SnapshotDocument snapshot, PlannedVocabularyEdge edge) =>
        edge.ReplacedMemberIds.Count == 0
        && snapshot.Relationships.Any(existing => existing.SubjectId == edge.SubjectId
                                                  && existing.PredicateId == edge.PredicateId
                                                  && existing.TargetId == edge.MemberId);

    // The types a Thing reaches through `is`, walked outwards level by level and bounded like every
    // other chain walk here.
    private static HashSet<Guid> TypesReachedFrom(
        SnapshotDocument snapshot, IReadOnlyDictionary<Guid, string> namesById, Guid thingId)
    {
        var reached = new HashSet<Guid>();
        var frontier = new List<Guid> { thingId };
        for (var depth = 0; depth < TypeChainDepth && frontier.Count > 0; depth++)
        {
            var next = new List<Guid>();
            foreach (var current in frontier)
                foreach (var edge in snapshot.Relationships)
                {
                    if (edge.SubjectId != current) continue;
                    if (!SnapshotWalk.IsPredicate(namesById, edge.PredicateId, PlatformPredicates.Is)) continue;
                    if (reached.Add(edge.TargetId)) next.Add(edge.TargetId);
                }

            frontier = next;
        }

        return reached;
    }

}
