using Microsoft.Extensions.Logging;
using vos.Service.Forage.Helpers;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Forage.Services;

// Turns the words a run's fetches wrote into the relationships the model declares. The declaration is
// read from one scoped snapshot after the fetches; the words come from the fetch responses, so nothing
// here races the observation drainer.
//
// A word the vocabulary does not hold writes no relationship and is reported naming the source, the subject,
// the property, the word and the vocabulary — neither silently dropped nor silently written. A stale
// relationship is removed before its replacement is written, so a re-graded assessment never carries two
// levels; a run that fails between the two leaves none, is reported, and the next run's fetch resolves
// again.
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

        var written = new List<PlannedVocabularyEdge>();
        var notWritten = new List<PlannedVocabularyEdge>();
        foreach (var edge in resolution.Edges)
            (await WriteAsync(edge, cancellationToken) ? written : notWritten).Add(edge);
        return new WordsResolved(written, notWritten, resolution.Unresolved);
    }

    private async Task<bool> WriteAsync(PlannedVocabularyEdge edge, CancellationToken cancellationToken)
    {
        foreach (var stale in edge.Replaces)
            if (!await _mycelium.DeleteRelationshipAsync(stale, cancellationToken))
            {
                // Writing beside a relationship that would not go would leave two answers; leaving the stale
                // one alone keeps exactly one, and the next run resolves again.
                _logger.LogError(
                    "Could not remove the stale vocabulary edge {RelationshipId} from {Subject}; "
                    + "'{Word}' stays unresolved", stale, edge.SubjectName, edge.Word);
                return false;
            }

        if (await _mycelium.CreateRelationshipAsync(
                edge.SubjectId, edge.PredicateId, edge.MemberId, cancellationToken))
        {
            _logger.LogInformation("Resolved '{Word}' into an edge for {Subject}", edge.Word, edge.SubjectName);
            return true;
        }

        _logger.LogError("Could not relate {Subject} to the member '{Word}' names; the word stays "
            + "unresolved until the next discovery run", edge.SubjectName, edge.Word);
        return false;
    }

    private async Task<SnapshotDocument?> ReadDeclarationsAsync(
        IReadOnlyList<FetchedWords> fetched, CancellationToken cancellationToken)
    {
        SubscribeResult subscribed;
        try
        {
            subscribed = await _subscriptions.SubscribeAsync(
                DiscoveredVocabularyResolver.SelectorFor(
                    fetched.Select(words => words.SubjectId).Distinct().ToList()),
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to read the discovered-vocabulary declarations");
            return null;
        }

        try
        {
            return subscribed.Snapshot;
        }
        finally
        {
            try
            {
                await _subscriptions.UnsubscribeAsync(subscribed.SubscriptionId, cancellationToken);
            }
            catch (Exception exception)
            {
                // The read already succeeded; failing to release the subscription must not lose it.
                _logger.LogWarning(exception,
                    "Failed to release the vocabulary subscription {SubscriptionId}",
                    subscribed.SubscriptionId);
            }
        }
    }
}

// What resolving a run's words came to: the relationships written, those that would not go, and the words no
// member of their vocabulary is named by.
public sealed record WordsResolved(
    IReadOnlyList<PlannedVocabularyEdge> Written,
    IReadOnlyList<PlannedVocabularyEdge> NotWritten,
    IReadOnlyList<UnresolvedWord> Unknown);
