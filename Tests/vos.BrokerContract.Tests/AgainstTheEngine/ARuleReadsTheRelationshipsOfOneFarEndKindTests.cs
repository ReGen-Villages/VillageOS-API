using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Shared;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

public class ARuleReadsTheRelationshipsOfOneFarEndKindTests : IClassFixture<TheEngine>
{
    private static readonly Role MeterRole = new("far-end-meter", RoleIdentity.Of("far-end-meter"));
    private static readonly Role PumpRole = new("far-end-pump", RoleIdentity.Of("far-end-pump"));

    private readonly TheEngine _engine;

    public ARuleReadsTheRelationshipsOfOneFarEndKindTests(TheEngine engine) => _engine = engine;

    [FactAgainstTheEngine]
    public async Task A_rule_naming_one_kind_carries_the_relationships_to_that_kind_and_to_a_kind_under_it()
    {
        var reservoir = await ReservoirFeedingOneOfEachAsync();

        var snapshot = await ReadAsync(reservoir.Id, new RelationshipRule { Predicate = "feeds", FarEndType = "FlowMeter" });

        FedBy(snapshot, reservoir.Id).Should().BeEquivalentTo([reservoir.Meter, reservoir.ClampOnMeter]);
    }

    [FactAgainstTheEngine]
    public async Task A_rule_naming_a_list_of_kinds_carries_the_relationships_to_any_of_them()
    {
        var reservoir = await ReservoirFeedingOneOfEachAsync();

        var snapshot = await ReadAsync(reservoir.Id,
            new RelationshipRule { Predicate = "feeds", FarEndType = ["FlowMeter", "Pump"] });

        FedBy(snapshot, reservoir.Id).Should().BeEquivalentTo([reservoir.Meter, reservoir.ClampOnMeter, reservoir.Pump]);
    }

    [FactAgainstTheEngine]
    public async Task A_rule_naming_one_role_carries_the_relationships_to_the_kind_it_binds()
    {
        var reservoir = await ReservoirFeedingOneOfEachAsync();

        var snapshot = await ReadAsync(reservoir.Id, new RelationshipRule { Predicate = "feeds", FarEndRole = MeterRole.Id });

        FedBy(snapshot, reservoir.Id).Should().BeEquivalentTo([reservoir.Meter, reservoir.ClampOnMeter]);
    }

    [FactAgainstTheEngine]
    public async Task A_rule_naming_a_list_of_roles_carries_the_relationships_to_any_kind_they_bind()
    {
        var reservoir = await ReservoirFeedingOneOfEachAsync();

        var snapshot = await ReadAsync(reservoir.Id,
            new RelationshipRule { Predicate = "feeds", FarEndRole = [MeterRole.Id, PumpRole.Id] });

        FedBy(snapshot, reservoir.Id).Should().BeEquivalentTo([reservoir.Meter, reservoir.ClampOnMeter, reservoir.Pump]);
    }

    [FactAgainstTheEngine]
    public async Task A_rule_on_incoming_relationships_judges_the_subject_as_the_far_end()
    {
        await DeclareTheKindsOnceAsync();
        var measures = await _engine.DeclareOnceAsync("measures");
        var reservoir = await _engine.DeclareAsync("Lower reservoir");
        var meter = await MemberOfAsync("FlowMeter");
        var valve = await MemberOfAsync("Valve");
        await _engine.RelateAsync(meter, measures, reservoir);
        await _engine.RelateAsync(valve, measures, reservoir);

        var snapshot = await ReadAsync(reservoir,
            new RelationshipRule { Predicate = "measures", Direction = "incoming", FarEndType = "FlowMeter" });

        snapshot.Relationships.Should().ContainSingle()
            .Which.Should().Match<SnapshotRelationship>(edge => edge.SubjectId == meter && edge.TargetId == reservoir);
    }

    [FactAgainstTheEngine]
    public async Task A_rule_naming_its_far_end_kind_both_by_name_and_by_role_is_refused()
    {
        var reservoir = await ReservoirFeedingOneOfEachAsync();

        var reading = () => ReadAsync(reservoir.Id,
            new RelationshipRule { Predicate = "feeds", FarEndType = "FlowMeter", FarEndRole = MeterRole.Id });

        (await reading.Should().ThrowAsync<SubscriptionRefusedException>())
            .Which.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    private sealed record Reservoir(Guid Id, Guid Meter, Guid ClampOnMeter, Guid Pump);

    private async Task<Reservoir> ReservoirFeedingOneOfEachAsync()
    {
        await DeclareTheKindsOnceAsync();
        var feeds = await _engine.DeclareOnceAsync("feeds");
        var reservoir = new Reservoir(
            await _engine.DeclareAsync("Upper reservoir"),
            await MemberOfAsync("FlowMeter"),
            await MemberOfAsync("ClampOnFlowMeter"),
            await MemberOfAsync("Pump"));
        var valve = await MemberOfAsync("Valve");

        foreach (var farEnd in new[] { reservoir.Meter, reservoir.ClampOnMeter, reservoir.Pump, valve })
            await _engine.RelateAsync(reservoir.Id, feeds, farEnd);
        return reservoir;
    }

    private async Task DeclareTheKindsOnceAsync()
    {
        if (await _engine.FindAsync("FlowMeter") is not null) return;

        var isPredicate = await _engine.DeclareOnceAsync("is");
        var meter = await _engine.DeclareAsync("FlowMeter", isArchetype: true);
        var pump = await _engine.DeclareAsync("Pump", isArchetype: true);
        await _engine.DeclareAsync("Valve", isArchetype: true);
        await _engine.RelateAsync(await _engine.DeclareAsync("ClampOnFlowMeter", isArchetype: true), isPredicate, meter);
        await _engine.BindAsync(MeterRole, meter);
        await _engine.BindAsync(PumpRole, pump);
    }

    private async Task<Guid> MemberOfAsync(string kind)
    {
        var member = await _engine.DeclareAsync($"{kind} {Guid.NewGuid():N}");
        await _engine.RelateAsync(member, await _engine.TheOnlyThingNamedAsync("is"), await _engine.TheOnlyThingNamedAsync(kind));
        return member;
    }

    private static IEnumerable<Guid> FedBy(SnapshotDocument snapshot, Guid reservoir) =>
        snapshot.Relationships.Where(edge => edge.SubjectId == reservoir).Select(edge => edge.TargetId);

    private async Task<SnapshotDocument> ReadAsync(Guid subject, RelationshipRule rule)
    {
        var subscriptions = new SubscriptionClient(
            _engine.ClientFactory, NullLogger<SubscriptionClient>.Instance, TheEngine.Url, new LaunchToken(_engine.AdminToken));
        var subscribed = await subscriptions.SubscribeAsync(new SubscriptionSelector { Ids = [subject], Relationships = [rule] });
        await subscriptions.UnsubscribeAsync(subscribed.SubscriptionId);
        return subscribed.Snapshot;
    }
}
