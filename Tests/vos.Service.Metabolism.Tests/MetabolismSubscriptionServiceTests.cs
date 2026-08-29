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
using vos.Service.Shared.Subscriptions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Metabolism.Tests;

// Phase 5c (#5558): the SSE coordinator streams changes into the engine and keeps membership in step.
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
        public List<Guid> Added { get; } = new();
        public List<Guid> Removed { get; } = new();
        public List<ModelChangeEvent> ToStream { get; } = new();

        public Task<SubscribeResult> SubscribeAsync(SubscriptionSelector selector, CancellationToken ct = default)
            => Task.FromResult(new SubscribeResult(SubscriptionId, 0, new SnapshotDocument(0, new(), new())));

        public Task<AddObjectsResult> AddObjectsAsync(Guid subscriptionId, SubscriptionSelector selector, CancellationToken ct = default)
        {
            if (selector.Ids is not null) Added.AddRange(selector.Ids);
            return Task.FromResult(new AddObjectsResult(0, new SnapshotDocument(0, new(), new())));
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

    [Fact]
    public async Task Register_and_Cancel_adjust_subscription_membership()
    {
        var engine = new RecordingMetabolism();
        var fake = new FakeSubscriptionClient();
        var svc = Service(fake, engine);
        await svc.StartAsync(default);

        var relId = Guid.NewGuid();
        engine.Register(new SimulationConfig(
            relId.ToString(), "subj", "tgt", "SubjName", 1m, "u", "quantity", 60,
            DateTime.UtcNow, DateTime.UtcNow.AddYears(1), StartDelaySeconds: 99999m));
        await Settle.UntilAsync(() => fake.Added.Contains(relId),
            "Register announces the relationship to the subscription");

        engine.Cancel(relId.ToString());
        await Settle.UntilAsync(() => fake.Removed.Contains(relId),
            "Cancel drops the relationship from membership");

        await engine.StopAllAsync();
        await svc.StopAsync(default);
    }
}
