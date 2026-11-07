using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Shared;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;
using vos.Service.Tributary.Helpers;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// Only the engine resolves the selector; without relationship rules one registration's read grew with every
// other registration's `is` into the shared template.
public class TributaryReadsOneRegistrationsRelationshipsTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public TributaryReadsOneRegistrationsRelationshipsTests(TheEngine engine) => _engine = engine;

    [FactAgainstTheEngine]
    public async Task A_registration_read_carries_its_own_relationships_and_no_other_registrations()
    {
        var isPredicate = await _engine.DeclareOnceAsync("is");
        var authenticatesBy = await _engine.DeclareOnceAsync(EndpointKindRoles.Authentication);
        var binds = await _engine.DeclareOnceAsync(RoleBindings.BindsPredicateName);
        var observed = await _engine.DeclareOnceAsync(ObservedEdges.PredicateName);
        // The role Thing shares the predicate's word apart from its case, so it is declared by identifier
        // rather than found by name.
        await _engine.DeclareAsync("Observed", identifier: LandIntakeRoles.Observed.Id);
        await _engine.RelateAsync(LandIntakeRoles.Observed.Id, binds, observed);

        var template = await _engine.DeclareAsync("EsriEndpoint", isArchetype: true);
        var tokenExchange = await _engine.DeclareAsync("TokenExchangeAuthentication", isArchetype: true,
            properties: new Dictionary<string, object?> { ["tokenUrl"] = "" });
        await _engine.RelateAsync(template, authenticatesBy, tokenExchange);

        var countyParcels = await _engine.DeclareAsync("County parcels");
        var stateParcels = await _engine.DeclareAsync("State parcels");
        var floodZones = await _engine.DeclareAsync("Flood zones");
        foreach (var registration in new[] { countyParcels, stateParcels, floodZones })
            await _engine.RelateAsync(registration, isPredicate, template);

        var site = await _engine.DeclareAsync("Willow Bend");
        await _engine.RelateAsync(countyParcels, observed, site);
        await _engine.RelateAsync(stateParcels, observed, site);

        var snapshot = await ReadAsync(countyParcels);

        snapshot.Relationships.Should().Contain(edge =>
            edge.SubjectId == countyParcels && edge.PredicateId == isPredicate && edge.TargetId == template);
        snapshot.Relationships.Should().NotContain(edge => edge.SubjectId == stateParcels || edge.SubjectId == floodZones,
            "another registration's relationships are not this registration's read");
        EndpointKindResolver.Resolve(snapshot, countyParcels)[EndpointKindRoles.Authentication]
            .Should().BeEquivalentTo(new ResolvedKind("TokenExchangeAuthentication", ["tokenUrl"]));
        ObservedEdges.Resolve(snapshot, countyParcels).ObservedThingIds.Should().Equal(site);
    }

    private async Task<SnapshotDocument> ReadAsync(Guid endpoint)
    {
        var subscriptions = new SubscriptionClient(
            _engine.ClientFactory, NullLogger<SubscriptionClient>.Instance, TheEngine.Url, _engine.AdminToken);
        var subscribed = await subscriptions.SubscribeAsync(EndpointKindResolver.SelectorFor(endpoint));
        await subscriptions.UnsubscribeAsync(subscribed.SubscriptionId);
        return subscribed.Snapshot;
    }
}
