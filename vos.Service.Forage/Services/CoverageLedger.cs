using Microsoft.Extensions.Logging;
using vos.Service.Forage.Helpers;

namespace vos.Service.Forage.Services;

// A call a run still has to make, and the coverage it records the answer on. Null where the model
// declares no coverage vocabulary, or where minting one failed: the call is still made, because a model
// that never read the template still has sources worth fetching — it just keeps no record, and so asks
// again next time.
public sealed record OutstandingCall(CoveringSource Source, SourceCall Call, Guid? CoverageId, long Attempts);

// What the model already records about a run's calls, and what each call came to.
//
// A site was discovered exactly once ever while the state a run is dispatched by counted `observed`
// edges: nothing removes one, so the first source to answer ended the occupancy for every source. The
// coverage Things replace that count, and this is what fills them — so a partial failure leaves exactly
// the calls that failed outstanding, and a source added to the catalogue later reaches a site already
// discovered.
public sealed class CoverageLedger
{
    // Fields a run writes onto a coverage. They are the run's report, per call and durable, rather than
    // the response body the broker discarded and the log line it became.
    private const string ResolvedAtProperty = "resolvedAt";
    private const string LastAttemptAtProperty = "lastAttemptAt";
    private const string AttemptsProperty = "attempts";
    private const string FailureReasonProperty = "failureReason";

    // On the Site and on the catalogue source alike: when the coverage between it and its counterparts
    // was last matched. One name from both sides, because one run matches either — a site's coverage is
    // the sources that cover it, a source's is the sites it covers.
    public const string CoverageMatchedAtProperty = "coverageMatchedAt";

    private readonly ICoverageWriter _writer;
    private readonly TimeProvider _clock;
    private readonly ILogger<CoverageLedger> _logger;

    public CoverageLedger(ICoverageWriter writer, TimeProvider clock, ILogger<CoverageLedger> logger)
    {
        _writer = writer;
        _clock = clock;
        _logger = logger;
    }

    // Every call whose answer is not already in, with the coverage to record it on — minting one for
    // any call the model has none for. A call whose coverage resolved is left out, which is the whole of
    // "a source that already answered is not called again while its answer stands".
    public async Task<IReadOnlyList<OutstandingCall>> OutstandingAsync(
        Coverage coverage, CancellationToken cancellationToken)
    {
        var recorded = coverage.Recorded.ToDictionary(entry => (entry.SubjectId, entry.SourceId));
        var outstanding = new List<OutstandingCall>();

        foreach (var source in coverage.Covering)
            foreach (var call in source.Calls)
            {
                if (recorded.TryGetValue((call.SubjectId, source.SourceId), out var already))
                {
                    if (already.Resolved) continue;
                    outstanding.Add(new OutstandingCall(source, call, already.CoverageId, already.Attempts));
                    continue;
                }

                // Fetched either way. A mint that could not happen leaves the call unrecorded rather
                // than undone: dropping it would make a model missing the vocabulary discover nothing
                // at all, which is worse than the once-ever discovery this replaces.
                outstanding.Add(new OutstandingCall(
                    source, call, await MintAsync(source, call, coverage.Vocabulary, cancellationToken), 0));
            }

        return outstanding;
    }

    // The covering sources, holding only the calls still to make. A source every one of whose calls has
    // been answered drops out entirely rather than being called with an empty list.
    public static IReadOnlyList<CoveringSource> CallsStillToMake(
        IReadOnlyList<CoveringSource> covering, IReadOnlyList<OutstandingCall> outstanding)
    {
        var wanted = outstanding
            .GroupBy(call => call.Source.SourceId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<SourceCall>)[.. group.Select(call => call.Call)]);

        return
        [
            .. covering
                .Where(source => wanted.ContainsKey(source.SourceId))
                .Select(source => source with { Calls = wanted[source.SourceId] })
        ];
    }

    // Each outcome against the coverage of the call it came from, matched by identity rather than by
    // name: two Things may share a name, and an answer written onto the wrong coverage would report one
    // source's outage as another's.
    public async Task RecordAllAsync(
        IReadOnlyList<OutstandingCall> outstanding, DiscoveryReport report, CancellationToken cancellationToken)
    {
        var byCall = outstanding.ToDictionary(call => (call.Call.SubjectId, call.Source.SourceId));

        foreach (var outcome in report.Resolved.Concat(report.Unresolved))
        {
            // An outcome naming neither came from no call this run made, so there is no coverage it
            // belongs on. Recording it against a guess would put one source's answer on another's.
            if (outcome.SubjectId is not { } subjectId || outcome.SourceId is not { } sourceId) continue;
            if (byCall.TryGetValue((subjectId, sourceId), out var call))
                await RecordAsync(call, outcome, cancellationToken);
        }
    }

    // What one call came to. A resolved call is stamped and stops being asked; a failed one keeps the
    // provider's own words and the count of how often it has been tried, and stays outstanding.
    public async Task RecordAsync(OutstandingCall outstanding, SourceOutcome outcome, CancellationToken cancellationToken)
    {
        if (outstanding.CoverageId is not { } coverageId) return;

        var now = _clock.GetUtcNow().UtcDateTime;
        await _writer.WriteFactAsync(coverageId, LastAttemptAtProperty, now, cancellationToken);
        await _writer.WriteFactAsync(coverageId, AttemptsProperty, outstanding.Attempts + 1, cancellationToken);

        if (outcome.Resolved)
        {
            await _writer.WriteFactAsync(coverageId, ResolvedAtProperty, now, cancellationToken);
            return;
        }

        await _writer.WriteFactAsync(
            coverageId, FailureReasonProperty, outcome.Reason ?? string.Empty, cancellationToken);
    }

    // Stamped whatever the run found, including nothing: a Thing no source covers was looked at, and
    // saying so is what tells it apart from one still waiting to be.
    public Task StampWorkedOutAsync(Guid thingId, CancellationToken cancellationToken)
        => _writer.WriteFactAsync(
            thingId, CoverageMatchedAtProperty, _clock.GetUtcNow().UtcDateTime, cancellationToken);

    // Minted and related in one step, because a Thing left unrelated reaches neither its subject nor its
    // source and no later run can tell it from one that was never minted.
    private async Task<Guid?> MintAsync(
        CoveringSource source, SourceCall call, CoverageVocabulary? vocabulary, CancellationToken cancellationToken)
    {
        if (vocabulary == null) return null;

        var minted = await _writer.MintAsync($"{call.SubjectName} × {source.Name}", cancellationToken);
        if (minted == null)
        {
            _logger.LogError("Could not mint the coverage of {Subject} by {Source}; the call stays outstanding",
                call.SubjectName, source.Name);
            return null;
        }

        var related =
            await _writer.RelateAsync(minted.Value, vocabulary.Is, vocabulary.Archetype, cancellationToken)
            && await _writer.RelateAsync(minted.Value, vocabulary.AppliesTo, call.SubjectId, cancellationToken)
            && await _writer.RelateAsync(minted.Value, vocabulary.SourcedFrom, source.SourceId, cancellationToken);

        if (related) return minted;

        _logger.LogError(
            "Minted the coverage of {Subject} by {Source} and could not relate it; it records nothing and " +
            "the call stays outstanding", call.SubjectName, source.Name);
        return null;
    }
}
