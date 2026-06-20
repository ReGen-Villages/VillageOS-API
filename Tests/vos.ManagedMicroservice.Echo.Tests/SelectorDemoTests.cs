// The snapshot-selector worked example. Verifies SelectorDemo subscribes for the requested slice,
// summarises the resolved closure, and unsubscribes — and that the selector serialises to the
// documented POST /api/subscriptions body (types + traverse) over the real SubscriptionClient.

using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.ManagedMicroservice.Echo.Services;
using vos.ManagedMicroservice.Shared.Subscriptions;
using vos.Tests.Shared;
using Xunit;

namespace vos.ManagedMicroservice.Echo.Tests;

public class SelectorDemoTests
{
    private const string MyceliumUrl = "http://localhost:7243";

    [Fact]
    public void SliceByTypeAndTraverse_BuildsTypeAndTraverseSelector()
    {
        var sel = SelectorDemo.SliceByTypeAndTraverse("Battery", "powers");

        sel.Types.Should().ContainSingle().Which.Should().Be("Battery");
        sel.All.Should().BeFalse();
        sel.Traverse.Should().ContainSingle();
        sel.Traverse![0].Predicate.Should().Be("powers");
        sel.Traverse[0].Direction.Should().Be("outgoing");
        sel.IncludeRelationships.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_SummarisesClosure_AndUnsubscribes()
    {
        var subId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var snapshot = new SnapshotDocument(
            Watermark: 42,
            Things: new()
            {
                Thing("Battery-1"),
                Thing("Inverter-7"),
            },
            Relationships: new() { Rel() });
        var fake = new FakeSubscriptionClient(new SubscribeResult(subId, 42, snapshot));

        var result = await new SelectorDemo(fake).RunAsync(SelectorDemo.SliceByTypeAndTraverse("Battery", "powers"));

        result.SubscriptionId.Should().Be(subId);
        result.Watermark.Should().Be(42);
        result.Things.Should().Be(2);
        result.Relationships.Should().Be(1);
        result.ThingNames.Should().BeEquivalentTo(new[] { "Battery-1", "Inverter-7" });
        fake.Subscribed.Should().NotBeNull();
        fake.Subscribed!.Types.Should().Contain("Battery");
        fake.Unsubscribed.Should().Be(subId); // demo releases the subscription
    }

    [Fact]
    public async Task SubscriptionClient_PostsSelectorBody_ToSubscriptionsRoute()
    {
        HttpRequestMessage? captured = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/subscriptions" && req.Method == HttpMethod.Post)
            {
                captured = req;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"subscriptionId":"11111111-1111-1111-1111-111111111111","watermark":5,"snapshot":{"watermark":5,"things":[],"relationships":[]}}""",
                        Encoding.UTF8, "application/json"),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = new SubscriptionClient(new TestHttpClientFactory(new HttpClient(handler)),
            NullLogger.Instance, MyceliumUrl, serviceToken: "tok");

        var result = await client.SubscribeAsync(SelectorDemo.SliceByTypeAndTraverse("Battery", "powers"));

        result.SubscriptionId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        captured.Should().NotBeNull();
        var body = await captured!.Content!.ReadAsStringAsync();
        body.Should().Contain("\"types\"").And.Contain("Battery");
        body.Should().Contain("\"traverse\"").And.Contain("powers");
    }

    private static SnapshotThing Thing(string name) =>
        new(Guid.NewGuid(), name, new(), new(), Array.Empty<string>(), Array.Empty<Guid>());

    private static SnapshotRelationship Rel() =>
        new(Guid.NewGuid(), "powers", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new(), new(), Array.Empty<string>());

    /// <summary>Records the selector it was subscribed with and the id it was asked to unsubscribe.</summary>
    private sealed class FakeSubscriptionClient(SubscribeResult result) : ISubscriptionClient
    {
        public SubscriptionSelector? Subscribed { get; private set; }
        public Guid? Unsubscribed { get; private set; }

        public Task<SubscribeResult> SubscribeAsync(SubscriptionSelector selector, CancellationToken ct = default)
        {
            Subscribed = selector;
            return Task.FromResult(result);
        }

        public Task UnsubscribeAsync(Guid subscriptionId, CancellationToken ct = default)
        {
            Unsubscribed = subscriptionId;
            return Task.CompletedTask;
        }

        public Task<AddObjectsResult> AddObjectsAsync(Guid subscriptionId, SubscriptionSelector selector, CancellationToken ct = default)
            => Task.FromResult(new AddObjectsResult(result.Watermark, result.Snapshot));

        public Task RemoveObjectsAsync(Guid subscriptionId, IEnumerable<Guid> objectIds, CancellationToken ct = default)
            => Task.CompletedTask;

        public async IAsyncEnumerable<ModelChangeEvent> StreamAsync(Guid subscriptionId, long fromSequence,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
