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

// The SSE coordinator streams changes into the engine and keeps membership in step.
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
        public event Action? Reconnected { add { } remove { } }

        public Guid SubscriptionId { get; } = Guid.NewGuid();
        public List<SubscriptionSelector> Added { get; } = new();
        public List<Guid> Removed { get; } = new();
        public List<ModelChangeEvent> ToStream { get; } = new();

        public Task<SubscribeResult> SubscribeAsync(SubscriptionSelector selector, CancellationToken ct = default)
            => Task.FromResult(new SubscribeResult(SubscriptionId, 0, new SnapshotDocument(0, new(), new())));

        public Task<AddObjectsResult> AddObjectsAsync(Guid subscriptionId, SubscriptionSelector selector, CancellationToken ct = default)
        {
            lock (Added) Added.Add(selector);
            return Task.FromResult(new AddObjectsResult(0, new SnapshotDocument(0, new(), new())));
        }

        public bool Asked(Guid subjectId)
        {
            lock (Added) return Added.Any(selector => selector.Ids?.Contains(subjectId) == true);
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
            foreach (var e in ToStream) { yield return e; }
            // Stay open like a real stream until the service stops.
            try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { }
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
}
