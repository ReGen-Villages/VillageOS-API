using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Forage.Helpers;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// Several sites stand in one Place, and a site's read asks for every Place in the model. Without
// relationship rules the Place arrived with the isIn every other site wrote into it, so one site's read
// grew with the number of sites. The engine resolves the selector Forage sends; its unit tests cannot.
public class ForageReadsOneSitesRelationshipsTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public ForageReadsOneSitesRelationshipsTests(TheEngine engine) => _engine = engine;

    [FactAgainstTheEngine]
    public async Task A_site_read_carries_its_own_isIn_and_no_other_sites()
    {
        var isPredicate = await _engine.DeclareOnceAsync(CoveringSourceResolver.IsPredicateName);
        var isIn = await _engine.DeclareOnceAsync(CoveringSourceResolver.IsInPredicate);
        var covers = await _engine.DeclareOnceAsync(CoveringSourceResolver.CoversPredicate);
        var resolvedBy = await _engine.DeclareOnceAsync(CoveringSourceResolver.ResolvedByPredicate);
        var placeArchetype = await _engine.DeclareAsync("Place", isArchetype: true);
        await _engine.BindAsync(CoveringSourceResolver.PlaceArchetypeRole, placeArchetype);
        await _engine.BindAsync(CoveringSourceResolver.PlaceNestingRole, isIn);

        var portugal = await _engine.DeclareAsync("Portugal");
        await _engine.RelateAsync(portugal, isPredicate, placeArchetype);
        var willowBend = await _engine.DeclareAsync("Willow Bend");
        var alderCreek = await _engine.DeclareAsync("Alder Creek");
        var birchHollow = await _engine.DeclareAsync("Birch Hollow");
        foreach (var site in new[] { willowBend, alderCreek, birchHollow })
            await _engine.RelateAsync(site, isIn, portugal);

        var source = await _engine.DeclareAsync("Climate history");
        var registration = await _engine.DeclareAsync("climate-history");
        await _engine.RelateAsync(source, covers, portugal);
        await _engine.RelateAsync(source, resolvedBy, registration);

        var snapshot = await ReadAsync(willowBend);

        snapshot.Relationships.Should().Contain(edge =>
            edge.SubjectId == willowBend && edge.PredicateId == isIn && edge.TargetId == portugal);
        snapshot.Relationships.Should().NotContain(edge => edge.SubjectId == alderCreek || edge.SubjectId == birchHollow,
            "another site's relationships are not this site's read");
        CoveringSourceResolver.Resolve(snapshot, willowBend).Should().ContainSingle()
            .Which.Calls.Should().ContainSingle().Which.SubjectId.Should().Be(willowBend);
    }

    private async Task<SnapshotDocument> ReadAsync(Guid site)
    {
        var subscriptions = new SubscriptionClient(
            _engine.ClientFactory, NullLogger<SubscriptionClient>.Instance, TheEngine.Url, _engine.AdminToken);
        var subscribed = await subscriptions.SubscribeAsync(CoveringSourceResolver.SelectorFor(site));
        await subscriptions.UnsubscribeAsync(subscribed.SubscriptionId);
        return subscribed.Snapshot;
    }
}
