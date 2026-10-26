using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Shared;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;
using vos.Service.Tributary.Helpers;
using vos.Service.Tributary.Services;
using Xunit;
using TributaryClient = vos.Service.Tributary.Services.MyceliumClient;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// A model may get `observed` from Tributary or from the platform's open-data-sources template, in either order, and
// must end up with the role binding one predicate; otherwise every later run reads no predicate and writes a second
// provenance relationship.
public class TributaryRecordsWhichRegistrationObservedTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public TributaryRecordsWhichRegistrationObservedTests(TheEngine engine) => _engine = engine;

    private SubscriptionClient Subscriptions() => new(
        _engine.ClientFactory, NullLogger<SubscriptionClient>.Instance, TheEngine.Url, _engine.AdminToken);

    private TributaryClient Tributary() => new(
        _engine.ClientFactory, NullLogger<TributaryClient>.Instance, TheEngine.Url, TimeProvider.System,
        serviceToken: _engine.AdminToken);

    private Task<string?> ObserveAsync(Guid endpoint, Guid site, ObservedEdges edges) =>
        new ObservationIngestService(Tributary(), NullLogger<ObservationIngestService>.Instance, new ModelClock())
            .ObserveValueAsync(endpoint, site, "flowRegime", "steady", observedAt: null, edges);

    private async Task<(ObservedEdges Edges, SnapshotDocument Snapshot)> ReadAsync(Guid endpoint)
    {
        var subscriptions = Subscriptions();
        var subscribed = await subscriptions.SubscribeAsync(EndpointKindResolver.SelectorFor(endpoint));
        await subscriptions.UnsubscribeAsync(subscribed.SubscriptionId);
        return (ObservedEdges.Resolve(subscribed.Snapshot, endpoint), subscribed.Snapshot);
    }

    [FactAgainstTheEngine]
    public async Task Tributary_first_and_the_template_second_leaves_the_role_binding_one_predicate()
    {
        var isPredicate = await _engine.DeclareOnceAsync("is");
        var binds = await _engine.DeclareOnceAsync(RoleBindings.BindsPredicateName);
        var predicateRoot = await _engine.DeclareAsync("Predicate", isArchetype: true);
        await _engine.BindAsync(LandIntakeRoles.Predicate, predicateRoot);
        var endpoint = await _engine.DeclareAsync("climate-history");
        var site = await _engine.DeclareAsync("Willow Bend",
            properties: new Dictionary<string, object?> { ["flowRegime"] = "" });
        (await Tributary().SetPropertyModeAsync(site, "flowRegime", "SampledByTime", sampleSeconds: 60)).Should().BeTrue();

        var beforeAnyIngest = await ReadAsync(endpoint);
        beforeAnyIngest.Edges.PredicateId.Should().BeNull("the model binds no observed predicate yet");
        (await ObserveAsync(endpoint, site, beforeAnyIngest.Edges)).Should().BeNull();
        (await ReadAsync(endpoint)).Snapshot.Relationships.Should().Contain(edge =>
            edge.SubjectId == ObservedEdges.CreatedPredicateId && edge.PredicateId == isPredicate && edge.TargetId == predicateRoot,
            "Tributary puts the predicate it created under the root the predicate role binds");

        var templateDeclaration = await _engine.Admin.PostAsJsonAsync("/api/model/fragment", new
        {
            Name = "OpenDataSourcesTemplate",
            Things = new object[]
            {
                new { Id = Guid.Parse("98d642a8-4811-574d-afc9-c51fd68469bc"), Name = "observed" },
                new { Id = LandIntakeRoles.Observed.Id, Name = "Observed" },
            },
            Relationships = new object[]
            {
                new { Subject = LandIntakeRoles.Observed.Id, Predicate = binds, Target = Guid.Parse("98d642a8-4811-574d-afc9-c51fd68469bc") },
                new { Subject = LandIntakeRoles.Observed.Id, Predicate = isPredicate, Target = RoleIdentity.Of("role") },
                new { Subject = Guid.Parse("98d642a8-4811-574d-afc9-c51fd68469bc"), Predicate = isPredicate, Target = predicateRoot },
            },
        });
        templateDeclaration.IsSuccessStatusCode.Should().BeTrue(await templateDeclaration.Content.ReadAsStringAsync());

        var afterTheTemplate = await ReadAsync(endpoint);
        afterTheTemplate.Snapshot.Bound(LandIntakeRoles.Observed).Select(thing => thing.Id)
            .Should().Equal(ObservedEdges.CreatedPredicateId);
        afterTheTemplate.Edges.ObservedThingIds.Should().Equal(site);
        afterTheTemplate.Snapshot.Relationships.Should().ContainSingle(edge =>
            edge.SubjectId == ObservedEdges.CreatedPredicateId && edge.PredicateId == isPredicate && edge.TargetId == predicateRoot,
            "the created predicate is under the root the predicate role binds, once");

        (await ObserveAsync(endpoint, site, afterTheTemplate.Edges)).Should().BeNull();
        (await ReadAsync(endpoint)).Snapshot.Relationships
            .Count(edge => edge.SubjectId == endpoint && edge.PredicateId == ObservedEdges.CreatedPredicateId)
            .Should().Be(1, "a second run over the same site adds no provenance relationship");
    }
}
