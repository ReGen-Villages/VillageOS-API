using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Forage.Helpers;
using vos.Service.Forage.Services;
using vos.Service.Shared;
using vos.Service.Shared.Subscriptions;
using vos.Tests.Shared;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// The per-subject hold lets a data source's run and a site's run overlap, and both plan the call from a read holding no record of it.
public class ForageRecordsACoverageOnceTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public ForageRecordsACoverageOnceTests(TheEngine engine) => _engine = engine;

    [FactAgainstTheEngine]
    public async Task A_sources_run_and_a_sites_run_together_leave_one_coverage_record()
    {
        var vocabulary = new CoverageVocabulary(
            await _engine.DeclareAsync("SourceCoverage", isArchetype: true),
            await _engine.DeclareOnceAsync("is"),
            await _engine.DeclareAsync("appliesTo"),
            await _engine.DeclareAsync("sourcedFrom"));
        var site = await _engine.DeclareAsync("Willow Bend");
        var source = await _engine.DeclareAsync("Climate averages");
        var call = new SourceCall(site, "Willow Bend", new Dictionary<string, string>());
        var unrecorded = new Coverage([new CoveringSource(source, "Climate averages", "climate-averages", [call])], [], vocabulary);
        var writerLogger = new CapturingLogger<MyceliumRelationshipClient>();
        var ledgerLogger = new CapturingLogger<CoverageLedger>();

        var planned = await Task.WhenAll(
            Ledger(writerLogger, ledgerLogger).OutstandingAsync(unrecorded, default),
            Ledger(writerLogger, ledgerLogger).OutstandingAsync(unrecorded, default));

        var record = CoverageLedger.RecordIdentifierFor(site, source);
        planned.Should().AllSatisfy(calls => calls.Should().ContainSingle().Which.CoverageId.Should().Be(record));
        writerLogger.Lines.Should().BeEmpty();
        ledgerLogger.ErrorLines.Should().BeEmpty();
        var read = await new SubscriptionClient(
                _engine.ClientFactory, NullLogger<SubscriptionClient>.Instance,
                TheEngine.Url, new LaunchToken(_engine.AdminToken))
            .ReadSnapshotAsync(new() { Ids = [record], IncludeRelationships = true });
        read.Snapshot.Things.Should().ContainSingle(thing => thing.Id == record);
        read.Snapshot.Relationships.Where(edge => edge.SubjectId == record).Select(edge => edge.PredicateId)
            .Should().BeEquivalentTo([vocabulary.Is, vocabulary.AppliesTo, vocabulary.SourcedFrom]);
    }

    private CoverageLedger Ledger(
        CapturingLogger<MyceliumRelationshipClient> writerLogger, CapturingLogger<CoverageLedger> ledgerLogger) =>
        new(new MyceliumRelationshipClient(_engine.ClientFactory, writerLogger, TheEngine.Url, new LaunchToken(_engine.AdminToken)),
            new ModelClock(), ledgerLogger);
}
