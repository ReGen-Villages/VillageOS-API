using vos.Service.Forage.Helpers;
using vos.Service.Shared;

namespace vos.Service.Forage.Services;

// A call a run still has to make, and the coverage it records the answer on. Null where the model
// declares no coverage vocabulary, or where minting one failed: the call is still made, because a model
// that never read the template still has sources worth fetching — it just keeps no record, and so asks
// again next time.
public sealed record OutstandingCall(CoveringSource Source, SourceCall Call, Guid? CoverageId, long Attempts);

// What the model already records about a run's calls, and what each call came to.
//
// Counting `observed` relationships to decide whether a site needs a run would discover a site exactly
// once ever: nothing removes one, so the first source to answer would end the occupancy for every source.
// The coverage Things are counted instead, and this is what fills them — so a partial failure leaves
// exactly the calls that failed outstanding, and a source added to the catalogue later reaches a site
// already discovered.
public sealed class CoverageLedger
{
    // Fields a run writes onto a coverage. They are the run's report, per call and durable, rather than
    // a response body the broker discards and a log line.
    private const string ResolvedAtProperty = "resolvedAt";
    private const string LastAttemptAtProperty = "lastAttemptAt";
    private const string AttemptsProperty = "attempts";
    private const string FailureReasonProperty = "failureReason";

    // On the Site and on the catalogue source alike: when the coverage between it and its counterparts
    // was last matched. One name from both sides, because one run matches either — a site's coverage is
    // the sources that cover it, a source's is the sites it covers.
    public const string CoverageMatchedAtProperty = "coverageMatchedAt";

    private readonly ICoverageWriter _writer;
    private readonly ModelClock _clock;
    private readonly ILogger<CoverageLedger> _logger;

    public CoverageLedger(ICoverageWriter writer, ModelClock clock, ILogger<CoverageLedger> logger)
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
                    source, call, await WriteRecordAsync(source, call, coverage.Vocabulary, cancellationToken), 0));
            }

        return outstanding;
    }

    // What one call came to. A resolved call is stamped and stops being asked; a failed one keeps the
    // provider's own words and the count of how often it has been tried, and stays outstanding.
    public async Task RecordAsync(OutstandingCall outstanding, SourceOutcome outcome, CancellationToken cancellationToken)
    {
        if (outstanding.CoverageId is not { } coverageId) return;

        var now = _clock.GetUtcNow().UtcDateTime;
        await WriteOntoCoverageAsync(outstanding, coverageId, LastAttemptAtProperty, now, cancellationToken);
        await WriteOntoCoverageAsync(outstanding, coverageId, AttemptsProperty, outstanding.Attempts + 1, cancellationToken);

        if (outcome.Resolved)
        {
            await WriteOntoCoverageAsync(outstanding, coverageId, ResolvedAtProperty, now, cancellationToken);
            return;
        }

        await WriteOntoCoverageAsync(
            outstanding, coverageId, FailureReasonProperty, outcome.Reason ?? string.Empty, cancellationToken);
    }

    // Sent once more when refused, because a coverage missing a stamp reads as a source never asked until
    // the reconciler sends the whole run again.
    private async Task WriteOntoCoverageAsync(
        OutstandingCall outstanding, Guid coverageId, string property, object? value, CancellationToken cancellationToken)
    {
        if (await _writer.WriteFactAsync(coverageId, property, value, cancellationToken)) return;
        if (await _writer.WriteFactAsync(coverageId, property, value, cancellationToken)) return;

        _logger.LogError(
            "Could not write {Property} onto the coverage of {Subject} by {Source} in two attempts; the coverage " +
            "carries what earlier runs wrote", property, outstanding.Call.SubjectName, outstanding.Source.Name);
    }

    // Stamped whatever the run found, including nothing: a Thing no source covers was looked at, and
    // saying so is what tells it apart from one still waiting to be.
    public Task<bool> StampWorkedOutAsync(Guid thingId, CancellationToken cancellationToken)
        => _writer.WriteFactAsync(
            thingId, CoverageMatchedAtProperty, _clock.GetUtcNow().UtcDateTime, cancellationToken);

    // Worked out from the subject and the source, so a call planned twice before its answer lands, by a retried step
    // or by two runs at once, writes one record rather than two.
    public static Guid RecordIdentifierFor(Guid subjectId, Guid sourceId)
    {
        Span<byte> buffer = stackalloc byte[48];
        subjectId.TryWriteBytes(buffer[..16]);
        sourceId.TryWriteBytes(buffer[16..32]);
        RecordIdentifierSeed.TryWriteBytes(buffer[32..]);
        return new Guid(System.Security.Cryptography.MD5.HashData(buffer));
    }

    // Keeps a record's identifier apart from any other identifier worked out from the same two Things.
    private static readonly Guid RecordIdentifierSeed = new("0c5e7a9b-3f1d-4e62-9a8b-75790c0e0000");

    private async Task<Guid?> WriteRecordAsync(
        CoveringSource source, SourceCall call, CoverageVocabulary? vocabulary, CancellationToken cancellationToken)
    {
        if (vocabulary == null) return null;

        var recordId = RecordIdentifierFor(call.SubjectId, source.SourceId);
        if (await _writer.WriteCoverageAsync(recordId, $"{call.SubjectName} × {source.Name}",
                [(vocabulary.Is, vocabulary.Archetype), (vocabulary.AppliesTo, call.SubjectId), (vocabulary.SourcedFrom, source.SourceId)],
                cancellationToken))
            return recordId;

        _logger.LogError("Could not write the coverage of {Subject} by {Source}; the call stays outstanding",
            call.SubjectName, source.Name);
        return null;
    }
}
