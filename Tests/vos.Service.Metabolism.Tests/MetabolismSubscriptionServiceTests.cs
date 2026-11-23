using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using vos.Service.Metabolism.Models;
using vos.Service.Metabolism.Configuration;
using vos.Service.Metabolism.Services;
using vos.Service.Shared;
using vos.Service.Shared.Subscriptions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Metabolism.Tests;

// The subscription service streams changes into the engine and keeps what the subscription follows in
// step with the engine's simulations.
public class MetabolismSubscriptionServiceTests
{
    private sealed class RecordingMetabolism : Services.Metabolism
    {
        public RecordingMetabolism()
            : base(new MyceliumClient(Mock.Of<IHttpClientFactory>(), NullLogger<MyceliumClient>.Instance, "http://localhost:0", ResourceDirection.Produces),
                   NullLogger<Services.Metabolism>.Instance, ResourceDirection.Produces) { }

        public ConcurrentQueue<(string rel, string prop, object? val)> Updates { get; } = new();
        public override void UpdateProperty(string relationshipId, string propertyName, object? newValue)
            => Updates.Enqueue((relationshipId, propertyName, newValue));
    }

    private sealed class FakeSubscriptionClient : ISubscriptionClient
    {
        private readonly List<Guid> _opened = new();
        private TaskCompletionSource _streamLost = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event Action? Reconnected { add { } remove { } }

        public List<SubscriptionSelector> Added { get; } = new();
        public List<(Guid SubscriptionId, Guid SubjectId)> AddedTo { get; } = new();
        public List<Guid> Removed { get; } = new();
        public List<ModelChangeEvent> ToStream { get; } = new();
        public List<SnapshotRelationship> Held { get; } = new();
        public int SubscribesToTimeOut { get; set; }

        public IReadOnlyList<Guid> Opened { get { lock (_opened) return _opened.ToList(); } }

        // The client ends the stream of a subscription the broker no longer holds.
        public void LoseTheSubscription()
        {
            var lost = _streamLost;
            _streamLost = new(TaskCreationOptions.RunContinuationsAsynchronously);
            lost.SetResult();
        }

        public Task<SubscribeResult> SubscribeAsync(SubscriptionSelector selector, CancellationToken ct = default)
        {
            if (SubscribesToTimeOut-- > 0) throw new TaskCanceledException("the subscribe timed out");
            var subscriptionId = Guid.NewGuid();
            lock (_opened) _opened.Add(subscriptionId);
            return Task.FromResult(new SubscribeResult(subscriptionId, 0, new SnapshotDocument(0, new(), new())));
        }

        public Task<SnapshotReadResult> ReadSnapshotAsync(SubscriptionSelector selector, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<AddObjectsResult> AddObjectsAsync(Guid subscriptionId, SubscriptionSelector selector, CancellationToken ct = default)
        {
            lock (Added)
            {
                Added.Add(selector);
                AddedTo.AddRange(selector.Ids!.Select(subjectId => (subscriptionId, subjectId)));
            }
            var relationships = selector.IncludeSnapshot
                ? Held.Where(held => selector.Ids!.Contains(held.SubjectId)).ToList()
                : new();
            return Task.FromResult(new AddObjectsResult(0, new SnapshotDocument(0, new(), relationships)));
        }

        public bool Asked(Guid subjectId)
        {
            lock (Added) return Added.Any(selector => selector.Ids?.Contains(subjectId) == true);
        }

        public bool NewestSubscriptionHolds(Guid subjectId)
        {
            lock (Added) return Opened.Count > 1 && AddedTo.Contains((Opened[^1], subjectId));
        }

        public Task RemoveObjectsAsync(Guid subscriptionId, IEnumerable<Guid> objectIds, CancellationToken ct = default)
        {
            Removed.AddRange(objectIds);
            return Task.CompletedTask;
        }

        public Task UnsubscribeAsync(Guid subscriptionId, CancellationToken ct = default) => Task.CompletedTask;

        public async IAsyncEnumerable<ModelChangeEvent> StreamAsync(
            Guid subscriptionId, long fromSequence, [EnumeratorCancellation] CancellationToken ct = default)
        {
            var lost = _streamLost.Task;
            foreach (var e in ToStream) { yield return e; }
            // Stay open like a real stream until the subscription is lost or the service stops.
            try { await lost.WaitAsync(ct); } catch (OperationCanceledException) { }
        }
    }

    private static MetabolismSubscriptionService Service(ISubscriptionClient client, Services.Metabolism engine)
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns("Development");
        return new MetabolismSubscriptionService(client, engine, env.Object, NullLogger<MetabolismSubscriptionService>.Instance);
    }

    [Fact]
    public async Task Streams_property_changes_into_the_engine()
    {
        var engine = new RecordingMetabolism();
        var relId = Guid.NewGuid();
        var fake = new FakeSubscriptionClient();
        fake.ToStream.Add(new ModelChangeEvent
        {
            Sequence = 10, Kind = "RelationshipPropertyChanged", EntityId = relId,
            PropertyName = "quantity", Value = JsonSerializer.SerializeToElement(5),
        });
        var svc = Service(fake, engine);

        await svc.StartAsync(default);
        await Settle.UntilAsync(() => !engine.Updates.IsEmpty, "the streamed change reaches the engine");
        await svc.StopAsync(default);

        engine.Updates.Should().ContainSingle();
        engine.Updates.TryDequeue(out var u).Should().BeTrue();
        u.rel.Should().Be(relId.ToString());
        u.prop.Should().Be("quantity");
    }

    private static SimulationConfig ASimulation(Guid relationshipId, Guid subjectId) => new(
        relationshipId.ToString(), subjectId.ToString(), Guid.NewGuid().ToString(), "SubjName", 1m, "u", "quantity", 60,
        DateTime.UtcNow, DateTime.UtcNow.AddYears(1), StartDelaySeconds: 99999m);

    // The broker resolves Ids against Things only, so a relationship named there enters nothing.
    [Fact]
    public async Task Register_asks_for_the_relationship_through_its_subject_and_Cancel_drops_both()
    {
        var engine = new RecordingMetabolism();
        var fake = new FakeSubscriptionClient();
        var svc = Service(fake, engine);
        await svc.StartAsync(default);

        var relId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        engine.Register(ASimulation(relId, subjectId), HeldModelToken.None);
        await Settle.UntilAsync(() => fake.Asked(subjectId), "Register announces the relationship's subject to the subscription");

        var asked = fake.Added.Single();
        asked.Ids.Should().Equal(subjectId);
        asked.IncludeIsAncestors.Should().BeFalse();
        asked.IncludeSnapshot.Should().BeFalse();
        asked.IncludeRelationships.Should().BeTrue();
        asked.Relationships.Should().ContainSingle().Which.Direction.Should().Be("outgoing");

        engine.Cancel(relId.ToString());
        await Settle.UntilAsync(() => fake.Removed.Contains(relId), "Cancel drops the relationship from membership");
        fake.Removed.Should().Contain(subjectId, "no running simulation shares the subject any more");

        await engine.StopAllAsync();
        await svc.StopAsync(default);
    }

    [Fact]
    public async Task Cancelling_one_of_two_simulations_on_a_subject_keeps_the_subject_in_the_membership()
    {
        var engine = new RecordingMetabolism();
        var fake = new FakeSubscriptionClient();
        var svc = Service(fake, engine);
        await svc.StartAsync(default);

        var subjectId = Guid.NewGuid();
        var consumesWater = Guid.NewGuid();
        var consumesPower = Guid.NewGuid();
        engine.Register(ASimulation(consumesWater, subjectId), HeldModelToken.None);
        engine.Register(ASimulation(consumesPower, subjectId), HeldModelToken.None);

        engine.Cancel(consumesWater.ToString());
        await Settle.UntilAsync(() => fake.Removed.Contains(consumesWater), "Cancel drops the relationship from membership");

        fake.Removed.Should().NotContain(subjectId, "the other simulation's relationship is still reached through it");

        await engine.StopAllAsync();
        await svc.StopAsync(default);
    }

    [Fact]
    public async Task A_property_change_on_the_subject_itself_is_not_handed_to_the_engine()
    {
        var engine = new RecordingMetabolism();
        var fake = new FakeSubscriptionClient();
        var subjectId = Guid.NewGuid();
        fake.ToStream.Add(new ModelChangeEvent
        {
            Sequence = 10, Kind = "PropertyChanged", EntityId = subjectId,
            PropertyName = "quantity", Value = JsonSerializer.SerializeToElement(5),
        });
        var svc = Service(fake, engine);

        await svc.StartAsync(default);
        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(200));
        await svc.StopAsync(default);

        engine.Updates.Should().BeEmpty();
    }

    private static SnapshotRelationship ARelationship(Guid relationshipId, Guid subjectId, string statedValues) => new(
        relationshipId, null, subjectId, Guid.NewGuid(), Guid.NewGuid(),
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(statedValues)!
            .ToDictionary(stated => stated.Key, stated => new SnapshotProperty(stated.Value, null, null)),
        null, []);

    [Fact]
    public async Task A_lost_subscription_is_opened_again_with_every_running_simulations_subject()
    {
        var engine = new RecordingMetabolism();
        var fake = new FakeSubscriptionClient();
        var subjectId = Guid.NewGuid();
        engine.Register(ASimulation(Guid.NewGuid(), subjectId), HeldModelToken.None);
        var svc = Service(fake, engine);
        await svc.StartAsync(default);

        fake.LoseTheSubscription();

        await Settle.UntilAsync(() => fake.NewestSubscriptionHolds(subjectId), "the new subscription holds the running simulation's subject");
        await engine.StopAllAsync();
        await svc.StopAsync(default);
    }

    [Fact]
    public async Task A_simulation_registered_after_a_subscription_was_lost_joins_the_new_one()
    {
        var engine = new RecordingMetabolism();
        var fake = new FakeSubscriptionClient();
        var svc = Service(fake, engine);
        await svc.StartAsync(default);
        fake.LoseTheSubscription();

        var subjectId = Guid.NewGuid();
        engine.Register(ASimulation(Guid.NewGuid(), subjectId), HeldModelToken.None);

        await Settle.UntilAsync(() => fake.NewestSubscriptionHolds(subjectId), "the new subscription holds the simulation's subject");
        await engine.StopAllAsync();
        await svc.StopAsync(default);
    }

    [Fact]
    public async Task A_value_edited_while_no_subscription_was_held_reaches_the_engine_from_the_new_ones_snapshot()
    {
        var engine = new RecordingMetabolism();
        var fake = new FakeSubscriptionClient();
        var relationshipId = Guid.NewGuid();
        var otherRelationshipId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        engine.Register(ASimulation(relationshipId, subjectId), HeldModelToken.None);
        var svc = Service(fake, engine);
        await svc.StartAsync(default);
        await Settle.UntilAsync(() => fake.Asked(subjectId), "the simulation is followed");

        fake.Held.Add(ARelationship(relationshipId, subjectId, """{"quantity": 7, "unit": null}"""));
        fake.Held.Add(ARelationship(otherRelationshipId, subjectId, """{"quantity": 9}"""));
        fake.LoseTheSubscription();

        await Settle.UntilAsync(() => !engine.Updates.IsEmpty, "the edited value reaches the engine");
        await engine.StopAllAsync();
        await svc.StopAsync(default);
        var update = engine.Updates.Should().ContainSingle("a value that is not stated and another relationship's value are not the simulation's").Subject;
        update.rel.Should().Be(relationshipId.ToString());
        update.prop.Should().Be("quantity");
        ((JsonElement)update.val!).GetInt32().Should().Be(7);
    }

    [Fact]
    public async Task A_subscribe_that_times_out_is_tried_again()
    {
        var engine = new RecordingMetabolism();
        var fake = new FakeSubscriptionClient { SubscribesToTimeOut = 1 };
        var svc = Service(fake, engine);

        await svc.StartAsync(default);

        await Settle.UntilAsync(() => fake.Opened.Count == 1, "the second try opens the subscription");
        await svc.StopAsync(default);
    }
}
