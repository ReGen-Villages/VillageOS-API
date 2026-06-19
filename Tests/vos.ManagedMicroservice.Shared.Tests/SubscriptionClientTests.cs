using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.ManagedMicroservice.Shared.Subscriptions;
using vos.Tests.Shared;
using Xunit;

namespace vos.ManagedMicroservice.Shared.Tests;

public class SubscriptionClientTests
{
    private const string Url = "http://localhost:7243";
    private const string Token = "service-token";

    // Mirrors real IHttpClientFactory: a fresh HttpClient per call (so the base class can set
    // Timeout each time) sharing one mock handler.
    private sealed class FreshClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FreshClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private static (SubscriptionClient client, MockHttpMessageHandler handler) Build(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new MockHttpMessageHandler(respond);
        var client = new SubscriptionClient(new FreshClientFactory(handler), NullLogger.Instance, Url, Token)
        {
            ReconnectDelay = TimeSpan.FromMilliseconds(5),
        };
        return (client, handler);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Sse(string wire) =>
        new(HttpStatusCode.OK) { Content = new StringContent(wire, Encoding.UTF8, "text/event-stream") };

    [Fact]
    public async Task SubscribeAsync_returns_watermark_and_deserializes_snapshot()
    {
        var thingId = Guid.NewGuid();
        var body = $$"""
        {
          "subscriptionId": "{{Guid.NewGuid()}}",
          "watermark": 42,
          "snapshot": {
            "watermark": 42,
            "things": [{
              "id": "{{thingId}}", "name": "A",
              "properties": { "temp": { "value": 91, "typeInfo": "vos.Double" } },
              "inheritedProperties": {},
              "states": ["nominal"],
              "relationships": []
            }],
            "relationships": []
          }
        }
        """;
        var (client, _) = Build(_ => Json(body));

        var result = await client.SubscribeAsync(new SubscriptionSelector { Ids = new() { thingId } });

        result.Watermark.Should().Be(42);
        result.Snapshot.Things.Should().ContainSingle();
        var thing = result.Snapshot.Things[0];
        thing.Id.Should().Be(thingId);
        thing.Properties["temp"].Value.GetInt32().Should().Be(91);
        thing.Properties["temp"].TypeInfo.Should().Be("vos.Double");
        thing.States.Should().Contain("nominal");
    }

    [Fact]
    public async Task StreamAsync_yields_parsed_change_events()
    {
        var entityId = Guid.NewGuid();
        var wire = $"id: 43\nevent: PropertyChanged\n" +
                   $"data: {{\"Kind\":\"PropertyChanged\",\"EntityId\":\"{entityId}\",\"PropertyName\":\"temp\",\"Value\":92}}\n\n";
        var (client, _) = Build(_ => Sse(wire));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        ModelChangeEvent? got = null;
        await foreach (var e in client.StreamAsync(Guid.NewGuid(), fromSequence: 42, cts.Token))
        {
            got = e;
            break; // one event is enough; disposing the enumerator stops the stream
        }

        got.Should().NotBeNull();
        got!.Sequence.Should().Be(43);
        got.Kind.Should().Be("PropertyChanged");
        got.EntityId.Should().Be(entityId);
        got.PropertyName.Should().Be("temp");
        got.Value!.Value.GetInt32().Should().Be(92);
    }

    [Fact]
    public async Task StreamAsync_reconnects_and_resumes_with_last_event_id()
    {
        var entityId = Guid.NewGuid();
        var lastEventIds = new List<string?>();
        var connects = 0;

        var (client, _) = Build(req =>
        {
            lastEventIds.Add(req.Headers.TryGetValues("Last-Event-ID", out var v) ? v.First() : null);
            connects++;
            // Each connection delivers one event then EOF, forcing a reconnect.
            var seq = connects == 1 ? 50 : 51;
            return Sse($"id: {seq}\ndata: {{\"Kind\":\"PropertyChanged\",\"EntityId\":\"{entityId}\"}}\n\n");
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var sequences = new List<long>();
        await foreach (var e in client.StreamAsync(Guid.NewGuid(), fromSequence: 49, cts.Token))
        {
            sequences.Add(e.Sequence);
            if (sequences.Count == 2) break;
        }

        sequences.Should().Equal(50, 51);
        lastEventIds[0].Should().Be("49");  // initial resume from fromSequence
        lastEventIds[1].Should().Be("50");  // reconnect resumes after the last delivered event
    }
}
