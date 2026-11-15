using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Forage.Helpers;
using vos.Service.Forage.Services;
using vos.Service.Shared.Subscriptions;
using vos.Service.Shared;
using vos.Tests.Shared;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// The unit tests hand the resolver a snapshot; these ask the engine whether the selector Forage sends brings the
// study's relationship back, and what a second write of it answers.
public class ForageStartsASiteAnalysisOnceTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public ForageStartsASiteAnalysisOnceTests(TheEngine engine) => _engine = engine;

    [FactAgainstTheEngine]
    public async Task A_run_after_the_one_that_started_the_analysis_finds_it_started_and_writes_nothing()
    {
        var (site, _) = await ArrangeAsync("Willow Bend");
        var logger = new CapturingLogger<MyceliumRelationshipClient>();
        var spawner = Spawner(logger, NullLogger<AnalysisSpawner>.Instance);

        var first = await ReadAnalysisAsync(site);
        first.Triggers.Should().ContainSingle().Which.AlreadyRelated.Should().BeFalse();
        (await spawner.SpawnAsync(site, first, default)).Started.Should().BeTrue();

        var second = await ReadAnalysisAsync(site);
        second.Triggers.Should().ContainSingle().Which.Should().Be(
            new AnalysisTrigger("balancesEnergy", await _engine.TheOnlyThingNamedAsync("balancesEnergy"),
                await _engine.TheOnlyThingNamedAsync("EnergyBalance prototype"), AlreadyRelated: true));
        var spawn = await spawner.SpawnAsync(site, second, default);

        spawn.Started.Should().BeTrue("the relationship the first run wrote is still in the model");
        logger.Lines.Should().BeEmpty("a run finding the analysis started asks the engine for nothing it would refuse");
    }

    [FactAgainstTheEngine]
    public async Task A_run_that_read_the_analysis_unstarted_before_another_run_started_it_still_starts_it()
    {
        var (site, study) = await ArrangeAsync("Birch Ridge");
        var logger = new CapturingLogger<AnalysisSpawner>();
        var spawner = Spawner(NullLogger<MyceliumRelationshipClient>.Instance, logger);
        var bothRead = await ReadAnalysisAsync(site);

        var first = await spawner.SpawnAsync(site, bothRead, default);
        var second = await spawner.SpawnAsync(site, bothRead, default);

        first.Started.Should().BeTrue();
        second.Started.Should().BeTrue("the analysis it would have started is running");
        logger.ErrorLines.Should().BeEmpty();
        var connection = await _engine.TheOnlyThingNamedAsync("balancesEnergy");
        (await ReadSiteAsync(site)).Relationships
            .Count(edge => edge.SubjectId == study && edge.PredicateId == connection).Should().Be(1);
    }

    private AnalysisSpawner Spawner(
        ILogger<MyceliumRelationshipClient> clientLogger, ILogger<AnalysisSpawner> spawnerLogger) =>
        new(new MyceliumRelationshipClient(_engine.ClientFactory, clientLogger, TheEngine.Url, new LaunchToken(_engine.AdminToken)),
            spawnerLogger);

    // Declared once per engine, because the role binds exactly one kind of connection.
    private async Task<(Guid Site, Guid Study)> ArrangeAsync(string siteName)
    {
        var studies = await _engine.DeclareOnceAsync(CoveringSourceResolver.StudiesPredicate);
        if (await _engine.FindAsync("balancesEnergy") is null)
        {
            var has = await _engine.DeclareOnceAsync(CoveringSourceResolver.HasPredicate);
            var isPredicate = await _engine.DeclareOnceAsync(CoveringSourceResolver.IsPredicateName);
            var connectionArchetype = await _engine.DeclareAsync("SiteAnalysisConnection", isArchetype: true);
            await _engine.BindAsync(CoveringSourceResolver.SiteAnalysisConnectionRole, connectionArchetype);
            var prototype = await _engine.DeclareAsync("EnergyBalance prototype", isArchetype: true);
            var connection = await _engine.DeclareAsync("balancesEnergy");
            var service = await _engine.DeclareAsync("balancesEnergy service");
            await _engine.RelateAsync(connection, isPredicate, connectionArchetype);
            await _engine.RelateAsync(connection, has, service);
            await _engine.RelateAsync(service, isPredicate, prototype);
        }

        var site = await _engine.DeclareAsync(siteName);
        var study = await _engine.DeclareAsync($"{siteName} study");
        await _engine.RelateAsync(study, studies, site);
        return (site, study);
    }

    private async Task<SnapshotDocument> ReadSiteAsync(Guid site)
    {
        var subscriptions = new SubscriptionClient(
            _engine.ClientFactory, NullLogger<SubscriptionClient>.Instance, TheEngine.Url, new LaunchToken(_engine.AdminToken));
        return (await subscriptions.ReadSnapshotAsync(CoveringSourceResolver.SelectorFor(site))).Snapshot;
    }

    private async Task<SiteAnalysis> ReadAnalysisAsync(Guid site) =>
        CoveringSourceResolver.AnalysisOf(await ReadSiteAsync(site), site)!;
}
