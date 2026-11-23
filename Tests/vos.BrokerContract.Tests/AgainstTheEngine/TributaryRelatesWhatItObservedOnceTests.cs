using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Shared;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;
using vos.Service.Tributary.Helpers;
using vos.Service.Tributary.Services;
using vos.Tests.Shared;
using Xunit;
using TributaryClient = vos.Service.Tributary.Services.MyceliumClient;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// Calls that read the endpoint before either has written both relate the site, and refusing the second would fail its slice for good.
public class TributaryRelatesWhatItObservedOnceTests : IClassFixture<TheEngine>
{
    private const string Property = "flowRegime";
    private static readonly DateTime FirstSlice = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly TheEngine _engine;

    public TributaryRelatesWhatItObservedOnceTests(TheEngine engine) => _engine = engine;

    private SubscriptionClient Subscriptions() => new(
        _engine.ClientFactory, NullLogger<SubscriptionClient>.Instance, TheEngine.Url, new LaunchToken(_engine.AdminToken));

    private TributaryClient Tributary() => new(
        _engine.ClientFactory, NullLogger<TributaryClient>.Instance, TheEngine.Url, TimeProvider.System,
        launchToken: new LaunchToken(_engine.AdminToken));

    private Task<string?> ObserveAsync(Guid endpoint, Guid site, ObservedEdges edges, string value, DateTime observedAt) =>
        new ObservationIngestService(Tributary(), new ModelClock())
            .ObserveValueAsync(endpoint, site, Property, value, observedAt, edges);

    private async Task<ObservedEdges> EdgesOfAsync(Guid endpoint)
    {
        var read = await Subscriptions().ReadSnapshotAsync(EndpointKindResolver.SelectorFor(endpoint));
        return ObservedEdges.Resolve(read.Snapshot, endpoint);
    }

    private async Task<(Guid Endpoint, Guid Site)> AnEndpointAndASiteItHasNotObservedAsync(string endpointName, string siteName)
    {
        var isPredicate = await _engine.DeclareOnceAsync("is");
        var binds = await _engine.DeclareOnceAsync(RoleBindings.BindsPredicateName);
        var declared = await _engine.Admin.PostAsJsonAsync("/api/model/fragment", new
        {
            Name = "ObservedDeclared",
            Things = new object[]
            {
                new { Id = ObservedEdges.CreatedPredicateId, Name = ObservedEdges.PredicateName },
                new { Id = LandIntakeRoles.Observed.Id, Name = "Observed" },
                new { Id = RoleIdentity.Of("role"), Name = "Role", IsArchetype = true },
            },
            Relationships = new object[]
            {
                new { Subject = LandIntakeRoles.Observed.Id, Predicate = binds, Target = ObservedEdges.CreatedPredicateId },
                new { Subject = LandIntakeRoles.Observed.Id, Predicate = isPredicate, Target = RoleIdentity.Of("role") },
            },
        });
        declared.IsSuccessStatusCode.Should().BeTrue(await declared.Content.ReadAsStringAsync());

        var endpoint = await _engine.DeclareAsync(endpointName);
        var site = await _engine.DeclareAsync(siteName, properties: new Dictionary<string, object?> { [Property] = "" });
        (await Tributary().SetPropertyModeAsync(site, Property, "SampledByTime", sampleSeconds: 60)).Should().BeTrue();
        return (endpoint, site);
    }

    private async Task<IReadOnlyList<string?>> ReadingsHeldAsync(Guid site)
    {
        var history = await _engine.Admin.GetFromJsonAsync<JsonElement>($"/api/things/{site}/properties/{Property}/history");
        return history.GetProperty("samples").EnumerateArray()
            .Where(sample => sample.GetProperty("source").GetString() == "observation")
            .Select(sample => sample.GetProperty("value").GetString())
            .ToList();
    }

    private async Task<int> ObservedRelationshipsAsync(Guid endpoint, Guid site)
    {
        var read = await Subscriptions().ReadSnapshotAsync(EndpointKindResolver.SelectorFor(endpoint));
        return read.Snapshot.Relationships.Count(edge =>
            edge.SubjectId == endpoint && edge.PredicateId == ObservedEdges.CreatedPredicateId && edge.TargetId == site);
    }

    [FactAgainstTheEngine]
    public async Task Two_first_calls_for_one_site_at_once_both_succeed_and_both_write_their_readings()
    {
        var (endpoint, site) = await AnEndpointAndASiteItHasNotObservedAsync("climate-history", "Willow Bend");
        var edges = await EdgesOfAsync(endpoint);
        edges.ObservedThingIds.Should().NotContain(site, "neither call has written yet");

        var answers = await Task.WhenAll(
            ObserveAsync(endpoint, site, edges, "steady", FirstSlice),
            ObserveAsync(endpoint, site, edges, "flashy", FirstSlice.AddDays(365)));

        answers.Should().Equal(new string?[] { null, null }, "a slice that finds its relationship already written has nothing to fail on");
        // Readings are queued and applied after the write answers.
        await Settle.UntilAsync(async () => (await ReadingsHeldAsync(site)).Count == 2, "both calls' readings on the site");
        (await ReadingsHeldAsync(site)).Should().BeEquivalentTo(new[] { "steady", "flashy" });
        (await ObservedRelationshipsAsync(endpoint, site)).Should().Be(1);
    }

    [FactAgainstTheEngine]
    public async Task A_relationship_the_model_refuses_fails_the_call_and_writes_no_reading()
    {
        var (endpoint, _) = await AnEndpointAndASiteItHasNotObservedAsync("air-quality", "Elder Field");
        var noSuchSite = Guid.NewGuid();

        var answer = await ObserveAsync(endpoint, noSuchSite, await EdgesOfAsync(endpoint), "steady", FirstSlice);

        answer.Should().Be("Failed to relate entity to endpoint.");
        (await ObservedRelationshipsAsync(endpoint, noSuchSite)).Should().Be(0);
    }
}
