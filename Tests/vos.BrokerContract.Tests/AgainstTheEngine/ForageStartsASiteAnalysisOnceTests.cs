using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Forage.Helpers;
using vos.Service.Forage.Services;
using vos.Service.Shared.Subscriptions;
using vos.Tests.Shared;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// The engine refuses a second copy of a relationship, so a run that finds the study already related to a
// service has to see that relationship in its own site read. Its unit tests hand the resolver a snapshot
// holding it; this asks the engine whether the selector Forage sends brings it back.
public class ForageStartsASiteAnalysisOnceTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public ForageStartsASiteAnalysisOnceTests(TheEngine engine) => _engine = engine;

    [Fact]
    public async Task A_run_after_the_one_that_started_the_analysis_finds_it_started_and_writes_nothing()
    {
        var studies = await _engine.DeclareOnceAsync(CoveringSourceResolver.StudiesPredicate);
        var has = await _engine.DeclareOnceAsync(CoveringSourceResolver.HasPredicate);
        var isPredicate = await _engine.DeclareOnceAsync(CoveringSourceResolver.IsPredicateName);
        var connectionArchetype = await _engine.DeclareAsync("SiteAnalysisConnection", isArchetype: true,
            properties: new Dictionary<string, object?> { [CoveringSourceResolver.SiteAnalysisConnectionFlag] = true });
        var prototype = await _engine.DeclareAsync("EnergyBalance prototype", isArchetype: true);

        var connection = await _engine.DeclareAsync("balancesEnergy");
        var service = await _engine.DeclareAsync("balancesEnergy service");
        var site = await _engine.DeclareAsync("Willow Bend");
        var study = await _engine.DeclareAsync("Willow Bend study");

        await _engine.RelateAsync(connection, isPredicate, connectionArchetype);
        await _engine.RelateAsync(connection, has, service);
        await _engine.RelateAsync(service, isPredicate, prototype);
        await _engine.RelateAsync(study, studies, site);

        var logger = new CapturingLogger<MyceliumRelationshipClient>();
        var spawner = new AnalysisSpawner(
            new MyceliumRelationshipClient(_engine.ClientFactory, logger, TheEngine.Url, _engine.AdminToken),
            NullLogger<AnalysisSpawner>.Instance);

        var first = await ReadAnalysisAsync(site);
        first.Triggers.Should().ContainSingle().Which.AlreadyRelated.Should().BeFalse();
        (await spawner.SpawnAsync(site, first, default)).Started.Should().BeTrue();

        var second = await ReadAnalysisAsync(site);
        second.Triggers.Should().ContainSingle().Which.Should().Be(
            new AnalysisTrigger("balancesEnergy", connection, prototype, AlreadyRelated: true));
        var spawn = await spawner.SpawnAsync(site, second, default);

        spawn.Started.Should().BeTrue("the relationship the first run wrote is still in the model");
        logger.Lines.Should().BeEmpty("a run finding the analysis started asks the engine for nothing it would refuse");
    }

    private async Task<SiteAnalysis> ReadAnalysisAsync(Guid site)
    {
        var subscriptions = new SubscriptionClient(
            _engine.ClientFactory, NullLogger<SubscriptionClient>.Instance, TheEngine.Url, _engine.AdminToken);
        var subscribed = await subscriptions.SubscribeAsync(CoveringSourceResolver.SelectorFor(site));
        await subscriptions.UnsubscribeAsync(subscribed.SubscriptionId);
        return CoveringSourceResolver.AnalysisOf(subscribed.Snapshot, site)!;
    }
}
