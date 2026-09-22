using Microsoft.Extensions.Logging;
using vos.Service.Forage.Helpers;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Forage.Services;

// What one model read answers about the calls a run is concerned with: the sources, each with the calls
// to it and the values addressing each; what the model already records of those calls; and the
// vocabulary a coverage is minted in. All of it comes from the same snapshot, so a run reads the model
// once for it.
public record Coverage(
    IReadOnlyList<CoveringSource> Covering,
    IReadOnlyList<RecordedCoverage> Recorded,
    CoverageVocabulary? Vocabulary);

// A site's: the sources covering it, the analysis to start once the run has fetched, and what the run
// needs to work out a division the model supplies none of — the two lookups to call, and the address a
// call about the site itself is made with, which says both where the site is and whether anything already
// supplies a division code.
public sealed record SiteCoverage(
    IReadOnlyList<CoveringSource> Covering,
    SiteAnalysis? Analysis,
    IReadOnlyList<RecordedCoverage> Recorded,
    CoverageVocabulary? Vocabulary,
    DivisionLookup? Lookup,
    IReadOnlyDictionary<string, string> Address) : Coverage(Covering, Recorded, Vocabulary);

// Reads the model in one scoped snapshot per question: what a dispatch named, a site's coverage, or the
// sites a source reaches.
//
// Null means the read failed, never that the answer is empty. Collapsing the two would report a gateway
// outage as "no source covers this site", which is the silently-short list this whole path exists to
// prevent — or as a source that reaches no site, which would then be stamped as offered to all of them.
public sealed class CoveringSourceService
{
    private readonly ISubscriptionClient _subscriptions;
    private readonly ILogger<CoveringSourceService> _logger;

    public CoveringSourceService(ISubscriptionClient subscriptions, ILogger<CoveringSourceService> logger)
    {
        _subscriptions = subscriptions;
        _logger = logger;
    }

    public Task<SubjectKind?> KindOfAsync(Guid subjectId, CancellationToken cancellationToken) =>
        ReadAsync(
            CoveringSourceResolver.KindSelectorFor(subjectId),
            snapshot => (SubjectKind?)CoveringSourceResolver.KindOf(snapshot, subjectId),
            $"what {subjectId} is",
            cancellationToken);

    public Task<SiteCoverage?> ForSiteAsync(Guid siteId, CancellationToken cancellationToken) =>
        ReadAsync(
            CoveringSourceResolver.SelectorFor(siteId),
            snapshot => new SiteCoverage(
                CoveringSourceResolver.Resolve(snapshot, siteId),
                CoveringSourceResolver.AnalysisOf(snapshot, siteId),
                CoveringSourceResolver.RecordedCoverageIn(snapshot),
                CoveringSourceResolver.CoverageVocabularyIn(snapshot),
                CoveringSourceResolver.DivisionLookupIn(snapshot),
                CoveringSourceResolver.AddressOf(snapshot, siteId)),
            $"which sources cover site {siteId}",
            cancellationToken);

    public Task<Coverage?> ForSourceAsync(Guid sourceId, CancellationToken cancellationToken) =>
        ReadAsync(
            CoveringSourceResolver.SelectorForSource(sourceId),
            snapshot => new Coverage(
                CoveringSourceResolver.Reach(snapshot, sourceId),
                CoveringSourceResolver.RecordedCoverageIn(snapshot),
                CoveringSourceResolver.CoverageVocabularyIn(snapshot)),
            $"which sites source {sourceId} reaches",
            cancellationToken);

    private async Task<T?> ReadAsync<T>(
        SubscriptionSelector selector,
        Func<SnapshotDocument, T> resolve,
        string question,
        CancellationToken cancellationToken)
    {
        SubscribeResult subscribed;
        try
        {
            subscribed = await _subscriptions.SubscribeAsync(selector, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to read {Question}", question);
            return default;
        }

        try
        {
            return resolve(subscribed.Snapshot);
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
                _logger.LogWarning(exception, "Failed to release the coverage subscription {SubscriptionId}",
                    subscribed.SubscriptionId);
            }
        }
    }
}
