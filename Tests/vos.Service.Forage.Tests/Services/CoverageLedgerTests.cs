using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Forage.Helpers;
using vos.Service.Forage.Services;
using Xunit;

namespace vos.Service.Forage.Tests.Services;

// Task #6789 — what a run records about each call, and which calls it makes at all.
//
// A site was discovered exactly once ever while the dispatching state counted `observed` edges: the
// first source to answer ended the occupancy for every source, so a partial failure was never retried
// and a source added later never reached an existing site. The coverage Things replace that count, and
// this decides what goes on them.
//
// Both halves fail quietly if they are wrong. A call skipped when its answer never landed leaves a value
// missing with nothing saying why; a call made when the answer already stands calls a provider that has
// nothing new to give.
public class CoverageLedgerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);

    private readonly RecordingWriter _writer = new();
    private readonly TimeProvider _clock = new FixedClock(Now);

    // The instants a run stamps come from the clock it is given, never from the machine's: a stamp read
    // off DateTime.UtcNow is one no test can pin and no replay can reproduce.
    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private CoverageLedger Ledger() => new(_writer, _clock, NullLogger<CoverageLedger>.Instance);

    private static readonly Guid SiteId = Guid.NewGuid();
    private static readonly CoverageVocabulary Vocabulary =
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    private static CoveringSource SourceCalling(Guid sourceId, params Guid[] subjects) => new(
        sourceId, "ThinkHazard", "ThinkHazardEndpoint",
        [.. subjects.Select(subject => new SourceCall(subject, "subject", new Dictionary<string, string>()))]);

    private static SiteCoverage Coverage(
        IReadOnlyList<CoveringSource> covering,
        IReadOnlyList<RecordedCoverage>? recorded = null) =>
        new(covering, null, recorded ?? [], Vocabulary);

    // Spelled out rather than defaulted through the helper above: "no vocabulary" is the case under
    // test, and a helper that filled one in would pass it for the wrong reason.
    private static SiteCoverage CoverageWithoutVocabulary(IReadOnlyList<CoveringSource> covering) =>
        new(covering, null, [], null);

    // A writer that answers and remembers, so a test reads as what the ledger decided rather than as
    // what a gateway happened to do.
    private sealed class RecordingWriter : ICoverageWriter
    {
        public readonly List<(Guid Subject, Guid Predicate, Guid Target)> Edges = new();
        public readonly List<(Guid Thing, string Property, object? Value)> Facts = new();
        public readonly List<string> Minted = new();

        public Guid? NextMint { get; set; } = Guid.NewGuid();
        public bool RelateSucceeds { get; set; } = true;

        public Task<Guid?> MintAsync(string name, CancellationToken cancellationToken)
        {
            Minted.Add(name);
            return Task.FromResult(NextMint);
        }

        public Task<bool> RelateAsync(Guid subjectId, Guid predicateId, Guid targetId, CancellationToken _)
        {
            if (RelateSucceeds) Edges.Add((subjectId, predicateId, targetId));
            return Task.FromResult(RelateSucceeds);
        }

        public Task<bool> WriteFactAsync(Guid thingId, string property, object? value, CancellationToken _)
        {
            Facts.Add((thingId, property, value));
            return Task.FromResult(true);
        }
    }

    // The whole of "a source that already answered is not called again while its answer stands".
    [Fact]
    public async Task ACallWhoseAnswerAlreadyLandedIsNotOutstanding()
    {
        var source = Guid.NewGuid();
        var coverage = Coverage(
            [SourceCalling(source, SiteId)],
            [new RecordedCoverage(Guid.NewGuid(), SiteId, source, Resolved: true, Attempts: 1)]);

        (await Ledger().OutstandingAsync(coverage, default)).Should().BeEmpty();
        _writer.Minted.Should().BeEmpty("a coverage already recorded is not minted a second time");
    }

    [Fact]
    public async Task ACallThatFailedBeforeIsOutstandingAgainstTheCoverageItAlreadyHas()
    {
        var source = Guid.NewGuid();
        var existing = Guid.NewGuid();
        var coverage = Coverage(
            [SourceCalling(source, SiteId)],
            [new RecordedCoverage(existing, SiteId, source, Resolved: false, Attempts: 2)]);

        var outstanding = (await Ledger().OutstandingAsync(coverage, default)).Should().ContainSingle().Subject;

        outstanding.CoverageId.Should().Be(existing);
        outstanding.Attempts.Should().Be(2);
        _writer.Minted.Should().BeEmpty();
    }

    // A source that resolves onto an archetype is called once per Thing the site has of it. One
    // assessment answered and another not has to leave exactly the second outstanding.
    [Fact]
    public async Task OneSourceAnsweringForOneSubjectAndNotAnotherLeavesOnlyTheOtherOutstanding()
    {
        var source = Guid.NewGuid();
        var answered = Guid.NewGuid();
        var silent = Guid.NewGuid();
        var coverage = Coverage(
            [SourceCalling(source, answered, silent)],
            [
                new RecordedCoverage(Guid.NewGuid(), answered, source, Resolved: true, Attempts: 1),
                new RecordedCoverage(Guid.NewGuid(), silent, source, Resolved: false, Attempts: 1),
            ]);

        var outstanding = await Ledger().OutstandingAsync(coverage, default);

        outstanding.Should().ContainSingle().Which.Call.SubjectId.Should().Be(silent);
    }

    [Fact]
    public async Task ACallNothingHasRecordedGetsACoverageMintedAndRelatedBothWays()
    {
        var source = Guid.NewGuid();
        var minted = Guid.NewGuid();
        _writer.NextMint = minted;

        var outstanding = (await Ledger().OutstandingAsync(Coverage([SourceCalling(source, SiteId)]), default))
            .Should().ContainSingle().Subject;

        outstanding.CoverageId.Should().Be(minted);
        _writer.Edges.Should().BeEquivalentTo(new[]
        {
            (minted, Vocabulary.Is, Vocabulary.Archetype),
            (minted, Vocabulary.AppliesTo, SiteId),
            (minted, Vocabulary.SourcedFrom, source),
        });
    }

    // A model that never read the template declaring the vocabulary still has sources worth fetching.
    // Dropping the call would discover nothing at all, which is worse than the once-ever discovery this
    // replaces — so it is made, and simply left unrecorded.
    [Fact]
    public async Task AModelWithNoVocabularyStillMakesTheCallAndRecordsNothing()
    {
        var coverage = CoverageWithoutVocabulary([SourceCalling(Guid.NewGuid(), SiteId)]);

        var outstanding = (await Ledger().OutstandingAsync(coverage, default)).Should().ContainSingle().Subject;

        outstanding.CoverageId.Should().BeNull();
        _writer.Minted.Should().BeEmpty();
    }

    [Fact]
    public async Task AMintTheModelRefusedLeavesTheCallToBeMadeAndUnrecorded()
    {
        _writer.NextMint = null;

        var outstanding = (await Ledger().OutstandingAsync(Coverage([SourceCalling(Guid.NewGuid(), SiteId)]), default))
            .Should().ContainSingle().Subject;

        outstanding.CoverageId.Should().BeNull();
    }

    // A Thing minted and then left unrelated reaches neither its subject nor its source, and no later
    // run can tell it from one never minted. Recording against it would put the answer somewhere nothing
    // reads, so the call is treated as unrecorded.
    [Fact]
    public async Task ACoverageMintedButNotRelatedIsNotRecordedAgainst()
    {
        _writer.RelateSucceeds = false;

        var outstanding = (await Ledger().OutstandingAsync(Coverage([SourceCalling(Guid.NewGuid(), SiteId)]), default))
            .Should().ContainSingle().Subject;

        outstanding.CoverageId.Should().BeNull();
    }

    [Fact]
    public async Task AResolvedCallStampsWhenItsAnswerLanded()
    {
        var coverageId = Guid.NewGuid();
        var call = new OutstandingCall(SourceCalling(Guid.NewGuid(), SiteId), Call(SiteId), coverageId, 0);

        await Ledger().RecordAsync(call, new SourceOutcome("ThinkHazard", true, null), default);

        _writer.Facts.Should().Contain((coverageId, "resolvedAt", Now.UtcDateTime));
        _writer.Facts.Should().NotContain(fact => fact.Property == "failureReason");
    }

    // The provider's own words, kept where a planner reaches them from the gap they explain — the report
    // that was a response body the broker discarded and then a log line nobody reads.
    [Fact]
    public async Task AFailedCallKeepsTheProvidersReasonAndStaysUnresolved()
    {
        var coverageId = Guid.NewGuid();
        var call = new OutstandingCall(SourceCalling(Guid.NewGuid(), SiteId), Call(SiteId), coverageId, 0);

        await Ledger().RecordAsync(call, new SourceOutcome("ThinkHazard", false, "503 from the provider"), default);

        _writer.Facts.Should().Contain((coverageId, "failureReason", (object?)"503 from the provider"));
        _writer.Facts.Should().NotContain(fact => fact.Property == "resolvedAt");
    }

    // Added to, not overwritten: the count is what a reader uses to tell a provider that failed once
    // from one that has failed every run since the site was submitted.
    [Fact]
    public async Task AnAttemptIsAddedToWhatEarlierRunsRecorded()
    {
        var coverageId = Guid.NewGuid();
        var call = new OutstandingCall(SourceCalling(Guid.NewGuid(), SiteId), Call(SiteId), coverageId, 4);

        await Ledger().RecordAsync(call, new SourceOutcome("ThinkHazard", false, "still down"), default);

        _writer.Facts.Should().Contain((coverageId, "attempts", (object?)5L));
        _writer.Facts.Should().Contain((coverageId, "lastAttemptAt", Now.UtcDateTime));
    }

    [Fact]
    public async Task ACallWithNoCoverageRecordsNothingRatherThanFailing()
    {
        var call = new OutstandingCall(SourceCalling(Guid.NewGuid(), SiteId), Call(SiteId), null, 0);

        await Ledger().RecordAsync(call, new SourceOutcome("ThinkHazard", true, null), default);

        _writer.Facts.Should().BeEmpty();
    }

    // Stamped whatever the run found, including nothing: a Thing no source covers was looked at, and
    // saying so is what tells it apart from one still waiting to be.
    [Fact]
    public async Task WorkingOutCoverageIsStampedOnTheThingItWasWorkedOutFor()
    {
        await Ledger().StampWorkedOutAsync(SiteId, default);

        _writer.Facts.Should().ContainSingle()
            .Which.Should().Be((SiteId, CoverageLedger.CoverageWorkedOutAtProperty, (object?)Now.UtcDateTime));
    }

    // A source every one of whose calls has been answered drops out of the run entirely. Left in with an
    // empty call list it would be reported as a source that resolved nothing, which is what a provider
    // outage looks like.
    [Fact]
    public void ASourceWithNothingOutstandingIsNotCalledAtAll()
    {
        var answered = SourceCalling(Guid.NewGuid(), SiteId);
        var outstanding = SourceCalling(Guid.NewGuid(), SiteId);
        var still = CoverageLedger.CallsStillToMake(
            [answered, outstanding],
            [new OutstandingCall(outstanding, Call(SiteId), Guid.NewGuid(), 0)]);

        still.Should().ContainSingle().Which.SourceId.Should().Be(outstanding.SourceId);
    }

    [Fact]
    public void ASourceKeepsOnlyTheCallsStillOutstanding()
    {
        var silent = Guid.NewGuid();
        var source = SourceCalling(Guid.NewGuid(), Guid.NewGuid(), silent);

        var still = CoverageLedger.CallsStillToMake(
            [source], [new OutstandingCall(source, Call(silent), Guid.NewGuid(), 0)]);

        still.Should().ContainSingle().Which.Calls.Should().ContainSingle()
            .Which.SubjectId.Should().Be(silent);
    }

    [Fact]
    public void NothingOutstandingLeavesNoSourceToCall()
    {
        CoverageLedger.CallsStillToMake([SourceCalling(Guid.NewGuid(), SiteId)], []).Should().BeEmpty();
    }

    // Matched by identity, never by name: two sources sharing a name would otherwise be written each
    // other's answers, and a resolved call would stamp a coverage whose source never answered.
    [Fact]
    public async Task AnOutcomeIsRecordedAgainstTheCallItCameFrom()
    {
        var first = SourceCalling(Guid.NewGuid(), SiteId);
        var second = SourceCalling(Guid.NewGuid(), SiteId);
        var firstCoverage = Guid.NewGuid();
        var secondCoverage = Guid.NewGuid();
        var outstanding = new[]
        {
            new OutstandingCall(first, Call(SiteId), firstCoverage, 0),
            new OutstandingCall(second, Call(SiteId), secondCoverage, 0),
        };
        var report = new DiscoveryReport(
            [new SourceOutcome(first.Name, true, null, SubjectId: SiteId, SourceId: first.SourceId)],
            [new SourceOutcome(second.Name, false, "503", SubjectId: SiteId, SourceId: second.SourceId)]);

        await Ledger().RecordAllAsync(outstanding, report, default);

        _writer.Facts.Should().Contain((firstCoverage, "resolvedAt", (object?)Now.UtcDateTime));
        _writer.Facts.Should().Contain((secondCoverage, "failureReason", (object?)"503"));
        _writer.Facts.Should().NotContain(fact => fact.Thing == secondCoverage && fact.Property == "resolvedAt");
    }

    private static SourceCall Call(Guid subject) => new(subject, "subject", new Dictionary<string, string>());
}
