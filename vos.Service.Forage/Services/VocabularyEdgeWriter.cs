using vos.Service.Forage.Helpers;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Forage.Services;

// Turns the words a run's fetches wrote into the relationships the model declares. The declaration is
// read from one scoped snapshot after the fetches; the words come from the fetch responses, so nothing
// here races the observation drainer.
//
// A word the vocabulary does not hold writes no relationship and is reported naming the source, the subject,
// the property, the word and the vocabulary — neither silently dropped nor silently written. A stale
// relationship is removed in the same write as its replacement, so a re-graded assessment carries one level,
// and a refused write leaves the old level, is reported, and the next run's fetch resolves again.
public sealed class VocabularyEdgeWriter
{
    private readonly ISubscriptionClient _subscriptions;
    private readonly MyceliumRelationshipClient _mycelium;
    private readonly ILogger<VocabularyEdgeWriter> _logger;

    public VocabularyEdgeWriter(
        ISubscriptionClient subscriptions,
        MyceliumRelationshipClient mycelium,
        ILogger<VocabularyEdgeWriter> logger)
    {
        _subscriptions = subscriptions;
        _mycelium = mycelium;
        _logger = logger;
    }

    // Null where the declarations could not be read, which is not the same as no word resolving.
    public async Task<WordsResolved?> ResolveAsync(DiscoveryReport report, CancellationToken cancellationToken)
    {
        var fetched = report.Resolved
            .Where(outcome => outcome.SubjectId is not null && outcome.Written is { Count: > 0 })
            .Select(outcome => new FetchedWords(
                outcome.SubjectId!.Value, outcome.Source, outcome.Written!))
            .ToList();
        if (fetched.Count == 0) return new WordsResolved([], [], []);

        if (await ReadDeclarationsAsync(fetched, cancellationToken) is not { } snapshot)
        {
            // The observations are already written and the site has left the state that starts a
            // run, so nothing retries this by itself — said out loud rather than left as words that
            // quietly never became relationships.
            _logger.LogError(
                "Could not read the vocabulary declarations for {Subjects}; the fetched words stay "
                + "unresolved until the next discovery run", string.Join(", ", fetched.Select(words => words.SubjectId).Distinct()));
            return null;
        }

        var resolution = DiscoveredVocabularyResolver.Resolve(snapshot, fetched);

        foreach (var unresolved in resolution.Unresolved)
            _logger.LogWarning(
                "Source {Source} answered '{Word}' for {Subject}.{Property}, which names no member of "
                + "{Vocabulary}; the word stays an observation and no edge is written",
                unresolved.Source, unresolved.Word, unresolved.SubjectName,
                unresolved.Property, unresolved.Vocabulary);

        var (written, notWritten, refused, faulted) = (new List<PlannedVocabularyEdge>(), new List<PlannedVocabularyEdge>(), new List<RefusedWord>(), new List<RefusedWord>());
        var unknown = resolution.Unresolved.ToList();
        foreach (var edge in resolution.Edges)
        {
            var outcome = await _mycelium.WriteRelationshipAsync(
                $"'{edge.Word}' for {edge.SubjectName}", edge.SubjectId, edge.PredicateId, edge.MemberId,
                edge.ReplacedMemberIds, cancellationToken);
            switch (outcome.Answer)
            {
                case WriteAnswer.Written:
                    _logger.LogInformation("Resolved '{Word}' into an edge for {Subject}", edge.Word, edge.SubjectName);
                    written.Add(edge);
                    break;
                case WriteAnswer.Refused:
                    _logger.LogWarning("The platform refused relating {Subject} to the member '{Word}' names, and will refuse it "
                        + "again: {Reason}", edge.SubjectName, edge.Word, outcome.Reason);
                    refused.Add(new RefusedWord(edge, outcome.Reason ?? string.Empty));
                    break;
                case WriteAnswer.Rejected:
                    switch (await WhatARejectionMeansAsync(edge, fetched, cancellationToken))
                    {
                        case (Rejection.TheWriteItself, _):
                            faulted.Add(new RefusedWord(edge, outcome.Reason ?? string.Empty));
                            break;
                        case (Rejection.TheWordNamesNoMemberNow, { } gone):
                            unknown.Add(gone);
                            break;
                        default:
                            notWritten.Add(edge);
                            break;
                    }
                    break;
                default:
                    _logger.LogError("Could not relate {Subject} to the member '{Word}' names: {Reason}",
                        edge.SubjectName, edge.Word, outcome.Reason);
                    notWritten.Add(edge);
                    break;
            }
        }
        return new WordsResolved(written, notWritten, unknown) { Refused = refused, Faulted = faulted };
    }

    private enum Rejection { TheWriteItself, TheModelChangedSinceTheRead, TheWordNamesNoMemberNow }

    // A 400 carries only words, so the model read again tells a write that lost a race from one that is malformed.
    private async Task<(Rejection, UnresolvedWord?)> WhatARejectionMeansAsync(
        PlannedVocabularyEdge edge, IReadOnlyList<FetchedWords> fetched, CancellationToken cancellationToken)
    {
        var forSubject = fetched.Where(words => words.SubjectId == edge.SubjectId).ToList();
        if (await ReadDeclarationsAsync(forSubject, cancellationToken) is not { } snapshot) return (Rejection.TheModelChangedSinceTheRead, null);

        var again = DiscoveredVocabularyResolver.Resolve(snapshot, forSubject);
        if (again.Edges.Any(planned => planned.Word == edge.Word && planned.PredicateId == edge.PredicateId && planned.MemberId == edge.MemberId))
            return (Rejection.TheWriteItself, null);
        return again.Unresolved.FirstOrDefault(word => word.Word == edge.Word && word.SubjectName == edge.SubjectName) is { } gone
            ? (Rejection.TheWordNamesNoMemberNow, gone)
            : (Rejection.TheModelChangedSinceTheRead, null);
    }

    private async Task<SnapshotDocument?> ReadDeclarationsAsync(
        IReadOnlyList<FetchedWords> fetched, CancellationToken cancellationToken)
    {
        try
        {
            var read = await _subscriptions.ReadSnapshotAsync(
                DiscoveredVocabularyResolver.SelectorFor(
                    fetched.Select(words => words.SubjectId).Distinct().ToList()),
                cancellationToken);
            return read.Snapshot;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to read the discovered-vocabulary declarations");
            return null;
        }
    }
}

// What resolving a run's words came to: the relationships written, those that would not go, and the words no
// member of their vocabulary is named by.
public sealed record WordsResolved(
    IReadOnlyList<PlannedVocabularyEdge> Written,
    IReadOnlyList<PlannedVocabularyEdge> NotWritten,
    IReadOnlyList<UnresolvedWord> Unknown)
{
    public IReadOnlyList<RefusedWord> Refused { get; init; } = [];
    public IReadOnlyList<RefusedWord> Faulted { get; init; } = [];
}

public sealed record RefusedWord(PlannedVocabularyEdge Edge, string Reason);
