using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Forage.Services;
using vos.Service.Shared;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// DivisionResolver relates a site to the Place its code names only when its read shows no such relationship, so two
// runs that read the site before either wrote both make the write.
public class ForageRelatesASiteToItsPlaceOnceTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public ForageRelatesASiteToItsPlaceOnceTests(TheEngine engine) => _engine = engine;

    [FactAgainstTheEngine]
    public async Task A_second_run_relating_the_site_to_its_place_is_answered_as_done()
    {
        var site = await _engine.DeclareAsync("Willow Bend");
        var isIn = await _engine.DeclareAsync("isIn");
        var region = await _engine.DeclareAsync("Alentejo");
        var writer = new MyceliumRelationshipClient(
            _engine.ClientFactory, NullLogger<MyceliumRelationshipClient>.Instance, TheEngine.Url, new LaunchToken(_engine.AdminToken));

        (await writer.RelateAsync(site, isIn, region, default)).Should().BeTrue();
        (await writer.RelateAsync(site, isIn, region, default)).Should().BeTrue("the site already stands in the region");

        var read = await new SubscriptionClient(
                _engine.ClientFactory, NullLogger<SubscriptionClient>.Instance, TheEngine.Url, new LaunchToken(_engine.AdminToken))
            .ReadSnapshotAsync(new() { Ids = [site, region], IncludeRelationships = true });
        read.Snapshot.Relationships.Count(edge => edge.SubjectId == site && edge.PredicateId == isIn).Should().Be(1);
    }
}
