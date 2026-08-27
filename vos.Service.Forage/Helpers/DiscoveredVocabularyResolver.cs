using System.Text.Json;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Forage.Helpers;

// What one fetch wrote onto one Thing, as the run learned it from the fetch response. The response is
// the one place the words can come from: an observation is accepted into a queue and applied later, so
// reading the model back straight after the call races the drainer.
public sealed record FetchedWords(Guid SubjectId, string Source, IReadOnlyDictionary<string, string> Values);

// One edge the run should write: the subject, the predicate the shape edge names, and the member the
// fetched word resolved to — with the edges it replaces where the subject already carries this
// predicate pointing elsewhere, so a re-graded assessment ends the run carrying one level.
public sealed record PlannedVocabularyEdge(
    Guid SubjectId,
    string SubjectName,
    Guid PredicateId,
    Guid MemberId,
    string Word,
    IReadOnlyList<Guid> Replaces);

// A word the vocabulary does not hold, reported rather than silently dropped or silently written: the
// observation stays on the series, no edge is written, and every name a reader needs is carried.
public sealed record UnresolvedWord(
    string Source, string SubjectName, string Property, string Word, string Vocabulary);

public sealed record VocabularyResolution(
    IReadOnlyList<PlannedVocabularyEdge> Edges, IReadOnlyList<UnresolvedWord> Unresolved);

// Resolves the words a run's fetches wrote into the edges the model declares (#6809; the declaration
// is platform User Story 6773). Everything is read from the declaration: the vocabulary archetype
// carries the generic mark and names the property its word arrives under, and an archetype-level edge
// names the Thing the word is written onto and the predicate the resolved edge is written through.
// Nothing here names a vocabulary, an archetype, a predicate or a property of any model — a project
// adding a vocabulary edits its model and deploys nothing.
public static class DiscoveredVocabularyResolver
{
    // The strings the platform's intake template writes and its seed validation holds declarations to.
    // The two repositories agree by these literals and by nothing else.
    public const string ArchetypeFlag = "__IsDiscoveredVocabularyArchetype";
    public const string ResolvedFromProperty = "resolvedFromProperty";

    private const string IsPredicateName = "is";

    // A subject reaches the shape edge's archetype through `is`, possibly via intermediate types. The
    // same handful-of-levels reasoning as Place nesting: nobody has needed a deeper type chain, and an
    // unused level costs one set expansion.
    private const int TypeChainDepth = 8;

    // The subjects' type chains, every marked vocabulary with its members, and the relationships
    // incident to all of them — which is what carries the shape edges, the members' `is` edges, and
    // the vocabulary edges the subjects already hold.
    public static SubscriptionSelector SelectorFor(IReadOnlyCollection<Guid> subjectIds) => new()
    {
        Ids = [.. subjectIds],
        Names = [IsPredicateName],
        MarkedTypes = [ArchetypeFlag],
        Traverse = [new TraverseRule { Predicate = IsPredicateName, Depth = TypeChainDepth }],
        IncludeRelationships = true,
    };

    public static VocabularyResolution Resolve(SnapshotDocument snapshot, IReadOnlyList<FetchedWords> fetched)
    {
        var thingsById = snapshot.Things.ToDictionary(thing => thing.Id);
        var namesById = thingsById.ToDictionary(entry => entry.Key, entry => entry.Value.Name ?? string.Empty);
        var declarations = Declarations(snapshot, thingsById, namesById);

        // Keyed by subject and predicate, so two fetches writing one word in one run plan one edge and
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

        // An edge already pointing at the resolved member needs nothing; it is planned above so a later
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

    // Every archetype carrying the mark as its own property, with the property its word arrives under,
    // its members by name, and the shape edges reaching it. A member name two Things carry resolves
    // nothing — relationship order is not defined, and picking either would resolve one word two ways
    // on two runs — so the word is left to be reported as unresolved.
    private static List<Declaration> Declarations(
        SnapshotDocument snapshot,
        IReadOnlyDictionary<Guid, SnapshotThing> thingsById,
        IReadOnlyDictionary<Guid, string> namesById)
    {
        var declarations = new List<Declaration>();
        foreach (var vocabulary in snapshot.Things)
        {
            if (!CarriesOwnFlag(vocabulary, ArchetypeFlag)) continue;
            if (OwnString(vocabulary, ResolvedFromProperty) is not { } word) continue;

            var members = new Dictionary<string, Guid>(StringComparer.Ordinal);
            var contested = new HashSet<string>(StringComparer.Ordinal);
            foreach (var memberId in MembersUnder(snapshot, namesById, vocabulary.Id))
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
                               && !IsPredicate(namesById, edge.PredicateId, IsPredicateName))
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
        var replaces = snapshot.Relationships
            .Where(edge => edge.SubjectId == subjectId && edge.PredicateId == predicateId
                           && edge.TargetId != memberId)
            .Select(edge => edge.Id)
            .ToList();

        return new PlannedVocabularyEdge(subjectId, subjectName, predicateId, memberId, word, replaces);
    }

    private static bool AlreadyInPlace(SnapshotDocument snapshot, PlannedVocabularyEdge edge) =>
        edge.Replaces.Count == 0
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
                    if (!IsPredicate(namesById, edge.PredicateId, IsPredicateName)) continue;
                    if (reached.Add(edge.TargetId)) next.Add(edge.TargetId);
                }

            frontier = next;
        }

        return reached;
    }

    // Every Thing that `is` the vocabulary archetype, directly or through an intermediate type — the
    // same walk the coverage read makes for the analysis connections, over this read's own snapshot.
    private static List<Guid> MembersUnder(
        SnapshotDocument snapshot, IReadOnlyDictionary<Guid, string> namesById, Guid archetypeId)
    {
        var reached = new HashSet<Guid> { archetypeId };
        var members = new List<Guid>();
        var frontier = new Queue<Guid>([archetypeId]);

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            foreach (var edge in snapshot.Relationships)
            {
                if (edge.TargetId != current) continue;
                if (!IsPredicate(namesById, edge.PredicateId, IsPredicateName)) continue;
                if (!reached.Add(edge.SubjectId)) continue;

                frontier.Enqueue(edge.SubjectId);
                members.Add(edge.SubjectId);
            }
        }

        return members;
    }

    private static bool CarriesOwnFlag(SnapshotThing thing, string flag) =>
        thing.Properties.TryGetValue(flag, out var property)
        && property.Value.ValueKind == JsonValueKind.True;

    private static string? OwnString(SnapshotThing thing, string name) =>
        thing.Properties.TryGetValue(name, out var property)
        && property.Value.ValueKind == JsonValueKind.String
            ? property.Value.GetString()
            : null;

    private static bool IsPredicate(
        IReadOnlyDictionary<Guid, string> namesById, Guid predicateId, string name) =>
        namesById.TryGetValue(predicateId, out var predicate)
        && string.Equals(predicate, name, StringComparison.OrdinalIgnoreCase);
}
