using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Shared;
using vos.Service.Shared.Subscriptions;
using vos.Tests.Shared;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

public class TheLibraryReadsASelectionTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public TheLibraryReadsASelectionTests(TheEngine engine) => _engine = engine;

    [FactAgainstTheEngine]
    public async Task The_read_answers_the_snapshot_a_subscription_answers_for_the_same_selector()
    {
        var selector = await AReservoirFeedingTwoFlowMetersAsync();
        var subscriptions = Client(_engine.ClientFactory);

        var read = await subscriptions.ReadSnapshotAsync(selector);
        var subscribed = await subscriptions.SubscribeAsync(selector);
        await subscriptions.UnsubscribeAsync(subscribed.SubscriptionId);

        read.Snapshot.Things.Should().HaveCountGreaterThan(2);
        read.Snapshot.Relationships.Should().NotBeEmpty();
        read.Watermark.Should().Be(subscribed.Watermark);
        Comparable(read.Snapshot).Should().Equal(Comparable(subscribed.Snapshot));
    }

    [FactAgainstTheEngine]
    public async Task The_read_sends_one_request_to_the_snapshots_route_and_the_broker_registers_no_subscription()
    {
        var selector = await AReservoirFeedingTwoFlowMetersAsync();
        var transport = new RecordingTransport(_engine.Transport);

        await Client(new PerCallHttpClientFactory(transport)).ReadSnapshotAsync(selector);

        var sent = transport.Sent.Should().ContainSingle().Subject;
        sent.Request.Should().Be("POST /api/snapshots");
        JsonDocument.Parse(sent.Answer).RootElement.TryGetProperty("subscriptionId", out _).Should().BeFalse();
    }

    [FactAgainstTheEngine]
    public async Task A_selector_the_broker_refuses_is_raised_with_its_status_and_words()
    {
        var unknown = $"Reservoir {Guid.NewGuid():N}";

        var reading = () => Client(_engine.ClientFactory).ReadSnapshotAsync(new SubscriptionSelector { Types = [unknown] });

        var refused = (await reading.Should().ThrowAsync<SubscriptionRefusedException>()).Which;
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        refused.Body.Should().Contain(unknown);
    }

    private SubscriptionClient Client(IHttpClientFactory factory) =>
        new(factory, NullLogger<SubscriptionClient>.Instance, TheEngine.Url, new LaunchToken(_engine.AdminToken));

    private async Task<SubscriptionSelector> AReservoirFeedingTwoFlowMetersAsync()
    {
        var isPredicate = await _engine.DeclareOnceAsync("is");
        var feeds = await _engine.DeclareOnceAsync("feeds");
        var flowMeter = await _engine.DeclareOnceAsync("FlowMeter", isArchetype: true);
        var reservoir = await _engine.DeclareAsync($"Upper reservoir {Guid.NewGuid():N}",
            properties: new Dictionary<string, object?> { ["capacity"] = 1200.0 });
        foreach (var meter in new[] { "Inlet meter", "Outlet meter" })
        {
            var member = await _engine.DeclareAsync($"{meter} {Guid.NewGuid():N}");
            await _engine.RelateAsync(member, isPredicate, flowMeter);
            await _engine.RelateAsync(reservoir, feeds, member);
        }

        return new SubscriptionSelector
        {
            Ids = [reservoir],
            Traverse = [new TraverseRule { Predicate = "feeds", Direction = "outgoing", Depth = 1 }],
            Relationships = [new RelationshipRule { Predicate = "feeds" }, new RelationshipRule { Predicate = "is" }],
        };
    }

    private static List<string> Comparable(SnapshotDocument snapshot) =>
    [
        .. snapshot.Things.OrderBy(thing => thing.Id).Select(thing => JsonSerializer.Serialize(thing)),
        .. snapshot.Relationships.OrderBy(relationship => relationship.Id).Select(relationship => JsonSerializer.Serialize(relationship)),
    ];

    private sealed class RecordingTransport(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public List<(string Request, string Answer)> Sent { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            await response.Content.LoadIntoBufferAsync(cancellationToken);
            var answer = await response.Content.ReadAsStringAsync(cancellationToken);
            lock (Sent) Sent.Add(($"{request.Method} {request.RequestUri!.AbsolutePath}", answer));
            return response;
        }
    }
}
