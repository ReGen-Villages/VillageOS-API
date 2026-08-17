using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Shared.Subscriptions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Shared.Tests;

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
    public async Task AddObjectsAsync_posts_selector_and_returns_incremental_snapshot()
    {
        var thingId = Guid.NewGuid();
        HttpRequestMessage? captured = null;
        var body = $$"""
        { "watermark": 99, "snapshot": { "watermark": 99,
          "things": [{ "id": "{{thingId}}", "name": "New", "properties": {}, "inheritedProperties": {}, "states": [], "relationships": [] }],
          "relationships": [] } }
        """;
        var (client, _) = Build(req => { captured = req; return Json(body); });

        var result = await client.AddObjectsAsync(Guid.NewGuid(), new SubscriptionSelector { Ids = new() { thingId } });

        captured!.Method.Should().Be(HttpMethod.Post);
        captured.RequestUri!.AbsoluteUri.Should().EndWith("/objects");
        result.Watermark.Should().Be(99);
        result.Snapshot.Things.Should().ContainSingle().Which.Id.Should().Be(thingId);
    }

    [Fact]
    public async Task SubscribeAsync_sends_marked_types_under_the_name_the_broker_binds()
    {
        // A selector field the broker does not bind is dropped silently, and the read then answers with
        // everything the traversal reached and none of what was asked for by mark — a short answer
        // wearing the shape of a complete one.
        HttpRequestMessage? captured = null;
        var body = $$"""
        { "subscriptionId": "{{Guid.NewGuid()}}", "watermark": 1,
          "snapshot": { "watermark": 1, "things": [], "relationships": [] } }
        """;
        var (client, _) = Build(req => { captured = req; return Json(body); });

        await client.SubscribeAsync(new SubscriptionSelector { MarkedTypes = ["__IsPumpArchetype"] });

        (await captured!.Content!.ReadAsStringAsync())
            .Should().Contain("\"markedTypes\":[\"__IsPumpArchetype\"]");
    }

    [Fact]
    public async Task RemoveObjectsAsync_sends_delete_with_ids()
    {
        HttpRequestMessage? captured = null;
        var (client, _) = Build(req => { captured = req; return new HttpResponseMessage(HttpStatusCode.OK); });

        await client.RemoveObjectsAsync(Guid.NewGuid(), new[] { Guid.NewGuid() });

        captured!.Method.Should().Be(HttpMethod.Delete);
        captured.RequestUri!.AbsoluteUri.Should().EndWith("/objects");
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

    [Fact]
    public async Task UnsubscribeAsync_sends_delete_to_the_subscription()
    {
        HttpRequestMessage? captured = null;
        var (client, _) = Build(req => { captured = req; return new HttpResponseMessage(HttpStatusCode.OK); });
        var subId = Guid.NewGuid();

        await client.UnsubscribeAsync(subId);

        captured!.Method.Should().Be(HttpMethod.Delete);
        captured.RequestUri!.AbsolutePath.Should().Be($"/api/subscriptions/{subId}");
    }

    [Fact]
    public async Task StreamAsync_stops_cleanly_when_connect_fails_and_token_is_cancelled()
    {
        // 500 -> EnsureSuccessStatusCode throws -> ConnectAsync returns null -> StreamAsync
        // delays-reconnect, which returns false once the token is cancelled -> yield break.
        var (client, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var events = new List<ModelChangeEvent>();
        await foreach (var e in client.StreamAsync(Guid.NewGuid(), 0, cts.Token))
            events.Add(e);

        events.Should().BeEmpty();
    }
}
