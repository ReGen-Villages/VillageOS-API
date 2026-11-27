using System.Net;
using System.Text;
using System.Text.Json;
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
    private static readonly Guid MeterRole = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid PumpRole = Guid.Parse("55555555-5555-5555-5555-555555555555");

    // A fresh HttpClient per call sharing one handler, as the real factory gives, because the base class
    // sets a timeout on each client.
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
        var client = new SubscriptionClient(new FreshClientFactory(handler), NullLogger.Instance, Url, new LaunchToken(Token))
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

    private static SubscriptionSelector EverySelectorField() => new()
    {
        Ids = [Guid.Parse("11111111-1111-1111-1111-111111111111")],
        Names = ["Upper reservoir"],
        Types = ["FlowMeter"],
        BoundTypes = [MeterRole],
        BoundArchetypes = [PumpRole],
        PropertyValues = new() { ["status"] = "open" },
        Traverse = [new TraverseRule { PredicateRole = PumpRole, Direction = "incoming", Depth = 2 }],
        IncludeIsAncestors = false,
        Relationships = [new RelationshipRule { Predicate = "feeds", FarEndRole = MeterRole }],
    };

    [Fact]
    public async Task ReadSnapshotAsync_sends_one_post_to_the_snapshots_route_with_the_selector_as_a_subscription_sends_it()
    {
        var subscribeAnswer = $$"""
        { "subscriptionId": "{{Guid.NewGuid()}}", "watermark": 1,
          "snapshot": { "watermark": 1, "things": [], "relationships": [] } }
        """;
        var (subscriber, subscribing) = Build(_ => Json(subscribeAnswer));
        await subscriber.SubscribeAsync(EverySelectorField());
        var (reader, reading) = Build(_ => Json("""{ "watermark": 1, "snapshot": { "watermark": 1, "things": [], "relationships": [] } }"""));

        await reader.ReadSnapshotAsync(EverySelectorField());

        var sent = reading.Requests.Should().ContainSingle().Subject;
        sent.Method.Should().Be(HttpMethod.Post);
        sent.RequestUri!.AbsoluteUri.Should().Be($"{Url}/api/snapshots");
        sent.Headers.Authorization!.ToString().Should().Be($"Bearer {Token}");
        (await sent.Content!.ReadAsStringAsync())
            .Should().Be(await subscribing.Requests.Single().Content!.ReadAsStringAsync());
    }

    [Fact]
    public async Task ReadSnapshotAsync_answers_the_watermark_and_snapshot_the_broker_answers()
    {
        var thingId = Guid.NewGuid();
        var (client, _) = Build(_ => Json($$"""
        { "watermark": 42, "snapshot": { "watermark": 42,
          "things": [{ "id": "{{thingId}}", "name": "Upper reservoir", "isArchetype": false,
            "properties": { "level": { "value": 3.5, "typeInfo": "vos.Double" } },
            "inheritedProperties": {}, "states": ["nominal"], "relationships": [] }],
          "relationships": [] } }
        """));

        var read = await client.ReadSnapshotAsync(new SubscriptionSelector { Ids = [thingId] });

        read.Watermark.Should().Be(42);
        read.Snapshot.Watermark.Should().Be(42);
        var thing = read.Snapshot.Things.Should().ContainSingle().Subject;
        thing.Id.Should().Be(thingId);
        thing.Name.Should().Be("Upper reservoir");
        thing.Properties["level"].Value.GetDouble().Should().Be(3.5);
        thing.States.Should().Equal("nominal");
    }

    [Fact]
    public async Task ReadSnapshotAsync_raises_a_refusal_with_the_platforms_status_and_words_naming_the_read()
    {
        var (client, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":"no Thing is named FlowMeter"}""", Encoding.UTF8, "application/json"),
        });

        var reading = () => client.ReadSnapshotAsync(new SubscriptionSelector { Types = ["FlowMeter"] });

        var refused = (await reading.Should().ThrowAsync<SubscriptionRefusedException>()).Which;
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        refused.Body.Should().Be("""{"error":"no Thing is named FlowMeter"}""");
        refused.Message.Should().Be("""the platform refused the read with 400: {"error":"no Thing is named FlowMeter"}""");
    }

    [Fact]
    public async Task SubscribeAsync_raises_a_refusal_naming_the_subscription()
    {
        var (client, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent("two Things are named FlowMeter", Encoding.UTF8, "text/plain"),
        });

        var subscribing = () => client.SubscribeAsync(new SubscriptionSelector { Types = ["FlowMeter"] });

        (await subscribing.Should().ThrowAsync<SubscriptionRefusedException>()).Which.Message
            .Should().Be("the platform refused the subscription with 409: two Things are named FlowMeter");
    }

    [Fact]
    public async Task ReadSnapshotAsync_throws_when_the_broker_answers_nothing()
    {
        var (client, _) = Build(_ => Json("null"));

        var reading = () => client.ReadSnapshotAsync(new SubscriptionSelector { Ids = [Guid.NewGuid()] });

        await reading.Should().ThrowAsync<InvalidOperationException>();
    }

    // A field the broker does not bind is dropped silently, so a reader asking by role would read the whole
    // model minus the Things it asked for, with nothing to say so.
    [Fact]
    public async Task SubscribeAsync_sends_the_role_terms_under_the_names_the_broker_binds()
    {
        HttpRequestMessage? captured = null;
        var body = $$"""
        { "subscriptionId": "{{Guid.NewGuid()}}", "watermark": 1,
          "snapshot": { "watermark": 1, "things": [], "relationships": [] } }
        """;
        var (client, _) = Build(req => { captured = req; return Json(body); });
        var members = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var alone = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var predicate = Guid.Parse("33333333-3333-3333-3333-333333333333");

        await client.SubscribeAsync(new SubscriptionSelector
        {
            BoundTypes = [members],
            BoundArchetypes = [alone],
            Traverse = [new TraverseRule { PredicateRole = predicate }],
            Relationships = [new RelationshipRule { PredicateRole = predicate }],
        });

        var sent = await captured!.Content!.ReadAsStringAsync();
        sent.Should().Contain($"\"boundTypes\":[\"{members}\"]");
        sent.Should().Contain($"\"boundArchetypes\":[\"{alone}\"]");
        sent.Should().Contain($"\"predicateRole\":\"{predicate}\"");
    }

    [Fact]
    public async Task SubscribeAsync_sends_the_property_values_under_the_name_the_broker_binds()
    {
        HttpRequestMessage? captured = null;
        var body = $$"""
        { "subscriptionId": "{{Guid.NewGuid()}}", "watermark": 1,
          "snapshot": { "watermark": 1, "things": [], "relationships": [] } }
        """;
        var (client, _) = Build(req => { captured = req; return Json(body); });

        await client.SubscribeAsync(new SubscriptionSelector
        {
            Types = ["Vessel"],
            PropertyValues = new Dictionary<string, string> { ["code"] = "A1" },
        });

        var sent = await captured!.Content!.ReadAsStringAsync();
        sent.Should().Contain("\"propertyValues\":{\"code\":\"A1\"}");
    }

    [Fact]
    public async Task SubscribeAsync_sends_the_relationship_rules_under_the_names_the_broker_binds()
    {
        // A field the broker does not bind is dropped silently, and the read then carries every relationship
        // touching the Thing — the whole history the rule was there to leave out.
        HttpRequestMessage? captured = null;
        var body = $$"""
        { "subscriptionId": "{{Guid.NewGuid()}}", "watermark": 1,
          "snapshot": { "watermark": 1, "things": [], "relationships": [] } }
        """;
        var (client, _) = Build(req => { captured = req; return Json(body); });

        await client.SubscribeAsync(new SubscriptionSelector
        {
            Relationships =
            [
                new RelationshipRule { Predicate = "holds" },
                new RelationshipRule { PredicateRole = MeterRole, Direction = "incoming" },
            ],
        });

        var sent = await captured!.Content!.ReadAsStringAsync();
        sent.Should().Contain("\"relationships\":[");
        sent.Should().Contain("\"predicate\":\"holds\"");
        sent.Should().Contain("\"direction\":\"outgoing\"");
        sent.Should().Contain($"\"predicateRole\":\"{MeterRole}\"");
        sent.Should().Contain("\"direction\":\"incoming\"");
    }

    [Fact]
    public async Task SubscribeAsync_sends_a_far_end_kind_as_one_name_or_a_list_of_names()
    {
        var sent = await SentSelectorAsync(new SubscriptionSelector
        {
            Relationships =
            [
                new RelationshipRule { Predicate = "feeds", FarEndType = "FlowMeter" },
                new RelationshipRule { Predicate = "feeds", FarEndType = ["FlowMeter", "Pump"] },
            ],
        });

        sent.Should().Contain("\"farEndType\":\"FlowMeter\"");
        sent.Should().Contain("\"farEndType\":[\"FlowMeter\",\"Pump\"]");
    }

    [Fact]
    public async Task SubscribeAsync_sends_a_far_end_role_as_one_identifier_or_a_list_of_identifiers()
    {
        var sent = await SentSelectorAsync(new SubscriptionSelector
        {
            Relationships =
            [
                new RelationshipRule { Predicate = "feeds", FarEndRole = MeterRole },
                new RelationshipRule { Predicate = "feeds", FarEndRole = [MeterRole, PumpRole] },
            ],
        });

        sent.Should().Contain($"\"farEndRole\":\"{MeterRole}\"");
        sent.Should().Contain($"\"farEndRole\":[\"{MeterRole}\",\"{PumpRole}\"]");
    }

    [Fact]
    public async Task SubscribeAsync_sends_a_rule_without_a_far_end_exactly_as_before_the_far_end_existed()
    {
        var sent = await SentSelectorAsync(new SubscriptionSelector
        {
            Relationships = [new RelationshipRule { Predicate = "holds" }],
        });

        JsonDocument.Parse(sent).RootElement.GetProperty("relationships")[0].GetRawText()
            .Should().Be("{\"predicate\":\"holds\",\"predicateRole\":null,\"direction\":\"outgoing\"}");
    }

    // A test stub reads a service's selector back from the request it was sent.
    [Fact]
    public void A_rule_read_back_from_the_wire_keeps_the_form_each_far_end_field_was_sent_in()
    {
        var rule = JsonSerializer.Deserialize<RelationshipRule>(
            $"{{\"farEndType\":\"FlowMeter\",\"farEndRole\":[\"{MeterRole}\",\"{PumpRole}\"]}}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        rule.FarEndType!.IsList.Should().BeFalse();
        rule.FarEndType.Should().Equal("FlowMeter");
        rule.FarEndRole!.IsList.Should().BeTrue();
        rule.FarEndRole.Should().Equal(MeterRole, PumpRole);
    }

    private static async Task<string> SentSelectorAsync(SubscriptionSelector selector)
    {
        HttpRequestMessage? captured = null;
        var body = $$"""
        { "subscriptionId": "{{Guid.NewGuid()}}", "watermark": 1,
          "snapshot": { "watermark": 1, "things": [], "relationships": [] } }
        """;
        var (client, _) = Build(request => { captured = request; return Json(body); });

        await client.SubscribeAsync(selector);

        return await captured!.Content!.ReadAsStringAsync();
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

    // The broker writes the kind inside data as the number of its enumeration member and on the event line
    // as its name. The event line is the one the client reads.
    [Fact]
    public async Task StreamAsync_names_a_change_by_its_event_line_when_data_carries_the_kind_as_a_number()
    {
        var entityId = Guid.NewGuid();
        var wire = $"id: 43\nevent: RelationshipPropertyChanged\n" +
                   $"data: {{\"Kind\":6,\"EntityId\":\"{entityId}\",\"PropertyName\":\"quantity\",\"Value\":7}}\n\n";
        var (client, _) = Build(_ => Sse(wire));

        using var cts = new CancellationTokenSource(Settle.Ceiling);
        ModelChangeEvent? got = null;
        await foreach (var e in client.StreamAsync(Guid.NewGuid(), fromSequence: 42, cts.Token))
        {
            got = e;
            break;
        }

        got.Should().NotBeNull();
        got!.Kind.Should().Be("RelationshipPropertyChanged");
        got.IsRelationshipPropertyChange.Should().BeTrue();
        got.EntityId.Should().Be(entityId);
        got.PropertyName.Should().Be("quantity");
    }

    [Fact]
    public async Task StreamAsync_drops_a_frame_that_names_no_event()
    {
        var wire = $"id: 43\ndata: {{\"Kind\":4,\"EntityId\":\"{Guid.NewGuid()}\"}}\n\n" +
                   $"id: 44\nevent: PropertyChanged\ndata: {{\"Kind\":4,\"EntityId\":\"{Guid.NewGuid()}\"}}\n\n";
        var (client, _) = Build(_ => Sse(wire));

        using var cts = new CancellationTokenSource(Settle.Ceiling);
        ModelChangeEvent? got = null;
        await foreach (var e in client.StreamAsync(Guid.NewGuid(), fromSequence: 42, cts.Token))
        {
            got = e;
            break;
        }

        got!.Sequence.Should().Be(44, "a frame with no event line is not a change");
    }

    [Fact]
    public async Task StreamAsync_yields_parsed_change_events()
    {
        var entityId = Guid.NewGuid();
        var wire = $"id: 43\nevent: PropertyChanged\n" +
                   $"data: {{\"Kind\":\"PropertyChanged\",\"EntityId\":\"{entityId}\",\"PropertyName\":\"temp\",\"Value\":92}}\n\n";
        var (client, _) = Build(_ => Sse(wire));

        using var cts = new CancellationTokenSource(Settle.Ceiling);
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

    // An entry rides beside the change that caused it and carries no id, so its sequence is
    // none; the change after it carries the sequence and is what moves the resume position.
    [Fact]
    public async Task StreamAsync_yields_an_entry_delivered_beside_a_change_and_resumes_from_the_change()
    {
        var thingId = Guid.NewGuid();
        var edgeId = Guid.NewGuid();
        var wire = $"event: ThingEntered\n" +
                   $"data: {{\"Kind\":\"ThingEntered\",\"EntityId\":\"{thingId}\",\"Thing\":{{\"Id\":\"{thingId}\",\"Name\":\"Willow Bend\",\"IsArchetype\":false,\"Properties\":{{}},\"States\":[]}}}}\n\n" +
                   $"id: 43\nevent: RelationshipCreated\n" +
                   $"data: {{\"Kind\":\"RelationshipCreated\",\"EntityId\":\"{edgeId}\",\"Relationship\":{{\"Id\":\"{edgeId}\",\"SubjectId\":\"{thingId}\",\"PredicateId\":\"{Guid.NewGuid()}\",\"TargetId\":\"{Guid.NewGuid()}\",\"Properties\":{{}},\"States\":[]}}}}\n\n";
        var lastEventIds = new List<string?>();
        var (client, _) = Build(req =>
        {
            lastEventIds.Add(req.Headers.TryGetValues("Last-Event-ID", out var v) ? v.First() : null);
            // The first connection delivers the entry alone and ends, so the reconnect shows what an
            // id-less event did to the resume position; the second delivers the change and ends.
            return Sse(lastEventIds.Count == 1 ? wire[..wire.IndexOf("id: 43")] : wire[wire.IndexOf("id: 43")..]);
        });

        using var cts = new CancellationTokenSource(Settle.Ceiling);
        var got = new List<ModelChangeEvent>();
        await foreach (var e in client.StreamAsync(Guid.NewGuid(), fromSequence: 42, cts.Token))
        {
            got.Add(e);
            if (got.Count == 2) break;
        }

        got.Select(e => e.Kind).Should().Equal("ThingEntered", "RelationshipCreated");
        got[0].Sequence.Should().Be(0, "an entry carries no id");
        got[0].Thing!.Name.Should().Be("Willow Bend");
        got[0].Thing!.Relationships.Should().BeNull("a Thing on the stream carries no incident-edge list");
        got[1].Sequence.Should().Be(43);
        got[1].Relationship!.SubjectId.Should().Be(thingId);
        lastEventIds.Should().Equal(new[] { "42", "42" }, "an entry moves the resume position nowhere; only the change after it does");
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
            return Sse($"id: {seq}\nevent: PropertyChanged\ndata: {{\"Kind\":4,\"EntityId\":\"{entityId}\"}}\n\n");
        });

        using var cts = new CancellationTokenSource(Settle.Ceiling);
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

    private static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    private static string AChange(long sequence) =>
        $"id: {sequence}\nevent: PropertyChanged\ndata: {{\"EntityId\":\"{Guid.NewGuid()}\"}}\n\n";

    private static async Task<List<ModelChangeEvent>> ReadToTheEndAsync(SubscriptionClient client, Guid subscriptionId)
    {
        using var cts = new CancellationTokenSource(Settle.Ceiling);
        var changes = new List<ModelChangeEvent>();
        await foreach (var change in client.StreamAsync(subscriptionId, fromSequence: 0, cts.Token))
            changes.Add(change);
        cts.IsCancellationRequested.Should().BeFalse("the stream ends by itself");
        return changes;
    }

    [Fact]
    public async Task StreamAsync_ends_when_the_broker_no_longer_holds_the_subscription()
    {
        var (client, handler) = Build(_ => Status(HttpStatusCode.NotFound));

        (await ReadToTheEndAsync(client, Guid.NewGuid())).Should().BeEmpty();

        handler.Requests.Should().HaveCount(1, "a subscription that is gone is not asked for again");
    }

    [Fact]
    public async Task StreamAsync_ends_when_a_dropped_subscription_is_found_gone()
    {
        var connects = 0;
        var (client, _) = Build(_ => ++connects == 1 ? Sse(AChange(1)) : Status(HttpStatusCode.NotFound));

        (await ReadToTheEndAsync(client, Guid.NewGuid())).Should().ContainSingle().Which.Sequence.Should().Be(1);

        connects.Should().Be(2);
    }

    private const string PositionNoLongerKept =
        "event: SnapshotRequired\ndata: {\"resumeAfter\":7,\"reason\":\"resume position precedes the retained commit-log floor\"}\n\n";

    [Fact]
    public async Task StreamAsync_closes_the_subscription_and_ends_when_the_platform_no_longer_keeps_its_position()
    {
        var subscriptionId = Guid.NewGuid();
        var (client, handler) = Build(request => request.Method == HttpMethod.Delete
            ? new HttpResponseMessage(HttpStatusCode.OK)
            : Sse(AChange(1) + PositionNoLongerKept));

        (await ReadToTheEndAsync(client, subscriptionId)).Should().ContainSingle().Which.Sequence.Should().Be(1);

        handler.Requests.Select(request => (request.Method, request.RequestUri!.AbsolutePath)).Should().Equal(
            (HttpMethod.Get, $"/api/subscriptions/{subscriptionId}/stream"),
            (HttpMethod.Delete, $"/api/subscriptions/{subscriptionId}"));
    }

    [Fact]
    public async Task StreamAsync_ends_when_the_platform_no_longer_keeps_its_position_even_if_the_close_fails()
    {
        var (client, handler) = Build(request => request.Method == HttpMethod.Delete
            ? throw new HttpRequestException("the platform went away")
            : Sse(PositionNoLongerKept));

        (await ReadToTheEndAsync(client, Guid.NewGuid())).Should().BeEmpty();

        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Reconnected_is_raised_when_the_stream_after_a_position_no_longer_kept_opens()
    {
        var behind = Guid.NewGuid();
        var (client, _) = Build(request => request.Method == HttpMethod.Delete
            ? new HttpResponseMessage(HttpStatusCode.OK)
            : request.RequestUri!.AbsolutePath.Contains(behind.ToString()) ? Sse(PositionNoLongerKept) : Sse(AChange(1)));
        var raised = 0;
        client.Reconnected += () => raised++;

        await ReadToTheEndAsync(client, behind);
        using var cts = new CancellationTokenSource(Settle.Ceiling);
        await foreach (var _ in client.StreamAsync(Guid.NewGuid(), fromSequence: 0, cts.Token))
            break;

        raised.Should().Be(1);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task StreamAsync_asks_again_after_any_other_refusal(HttpStatusCode refusal)
    {
        var connects = 0;
        var (client, _) = Build(_ => ++connects == 1 ? Status(refusal) : Sse(AChange(1)));

        using var cts = new CancellationTokenSource(Settle.Ceiling);
        ModelChangeEvent? got = null;
        await foreach (var change in client.StreamAsync(Guid.NewGuid(), fromSequence: 0, cts.Token))
        {
            got = change;
            break;
        }

        got!.Sequence.Should().Be(1);
    }

    [Fact]
    public async Task StreamAsync_waits_the_reconnect_delay_before_it_ends()
    {
        var (client, _) = Build(_ => Status(HttpStatusCode.NotFound));
        client.ReconnectDelay = TimeSpan.FromMilliseconds(300);
        var began = Environment.TickCount64;

        await ReadToTheEndAsync(client, Guid.NewGuid());

        (Environment.TickCount64 - began).Should().BeGreaterThanOrEqualTo(250,
            "a broker that holds no subscription it opens would otherwise be asked in a tight loop");
    }

    [Fact]
    public async Task Reconnected_is_raised_when_the_next_subscriptions_stream_opens_and_not_for_the_first()
    {
        var gone = Guid.NewGuid();
        var next = Guid.NewGuid();
        var connectsToTheFirst = 0;
        var (client, _) = Build(request => request.RequestUri!.AbsolutePath.Contains(next.ToString())
            ? Sse(AChange(1))
            : ++connectsToTheFirst == 1 ? Sse(AChange(1)) : Status(HttpStatusCode.NotFound));
        var raised = 0;
        client.Reconnected += () => raised++;

        await ReadToTheEndAsync(client, gone);
        raised.Should().Be(0, "the first stream opened once and then found its subscription gone");

        using var cts = new CancellationTokenSource(Settle.Ceiling);
        await foreach (var _ in client.StreamAsync(next, fromSequence: 0, cts.Token))
            break;
        raised.Should().Be(1);
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
        // A 500 leaves ConnectAsync with no connection, so StreamAsync waits to reconnect, and that wait gives
        // up once the token is cancelled, which ends the stream.
        using var cts = new CancellationTokenSource();
        var (client, _) = Build(_ =>
        {
            cts.Cancel();
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        var events = new List<ModelChangeEvent>();
        await foreach (var e in client.StreamAsync(Guid.NewGuid(), 0, cts.Token))
            events.Add(e);

        events.Should().BeEmpty();
    }
}
