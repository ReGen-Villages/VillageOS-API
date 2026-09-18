using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using vos.Service.Intake.Services;
using Xunit;

namespace vos.Service.Intake.Tests;

// Task #7060 — the two reads the tile design adds to the public page, proxied by this service because
// the page holds no credential and reaches this service alone: a reduction over one of the submission's
// own site's property series, under the page's ticket; and the satellite basemap's tiles, anonymous like
// the position lookups, forwarded to the registration the route names through the broker's endpoint
// route and served with the cache life the registration declares.
public class PublicPageProxyEndpointTests
{
    private const string TicketHeader = "X-Submission-Ticket";
    private const string EndpointForwardPath = "/api/endpoints/tributary";
    private const string ReducePath = "/api/temporal/reduce";
    private static readonly string ThisAddress = BrokerSnapshot.AddressOn("Willow Bend");
    private static readonly string OtherAddress = BrokerSnapshot.AddressOn("Alder Rise");
    private static readonly Guid ThisSite = BrokerSnapshot.SiteOf(WillowBend.SubmissionId);

    private static readonly Guid TileRegistration = Guid.Parse("00000000-0000-0000-0000-00000000c0de");

    private const string TheReducedAnswer = """{"Groups":[{"Key":"1","Value":27.4}],"Samples":8760,"UnusableSamples":0}""";
    private static readonly byte[] SomeTileBytes = Encoding.ASCII.GetBytes("not really a jpeg");

    /// <summary>The submissions model with the satellite tile registration the catalogue declares beside
    /// it: a standalone Thing carrying the mark, its cache life, and the tile address.</summary>
    private static BrokerSnapshot ModelWithTiles() =>
        BrokerSnapshot.WithTwoSubmissions().Thing(
            TileRegistration, "satellite-tiles",
            properties: new Dictionary<string, object>
            {
                [BasemapTileReader.RegistrationFlag] = new { typeInfo = "vos.Boolean", value = true },
                [BasemapTileReader.CacheLifeProperty] = new { typeInfo = "vos.LongInteger", value = 86400 },
                ["url"] = new { typeInfo = "vos.String", value = "https://tiles.example.test/{z}/{y}/{x}" },
            });

    private sealed class Broker
    {
        public List<string> EndpointCalls { get; } = [];
        public List<string> ReduceCalls { get; } = [];
        public HttpStatusCode ReduceAnswer { get; set; } = HttpStatusCode.OK;
        public string ReduceBody { get; set; } = TheReducedAnswer;
        public HttpStatusCode EndpointAnswer { get; set; } = HttpStatusCode.OK;

        public HttpResponseMessage Answer(HttpRequestMessage request, BrokerSnapshot model)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path == "/api/subscriptions")
                return ModelStub.Json(Reading(request, model));
            if (path.EndsWith("/ranges", StringComparison.Ordinal))
                return ModelStub.Json("""{"ThingId":"x","ThingName":"Study","OwnRanges":[],"InheritedRanges":[]}""");
            if (path == ReducePath)
            {
                ReduceCalls.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                return new HttpResponseMessage(ReduceAnswer)
                {
                    Content = new StringContent(ReduceBody, Encoding.UTF8, "application/json"),
                };
            }
            if (path == EndpointForwardPath)
            {
                EndpointCalls.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                if (EndpointAnswer != HttpStatusCode.OK) return new HttpResponseMessage(EndpointAnswer);
                return ModelStub.Json(JsonSerializer.Serialize(new
                {
                    contentType = "image/jpeg",
                    dataBase64 = Convert.ToBase64String(SomeTileBytes),
                    byteLength = SomeTileBytes.Length,
                }));
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        private static string Reading(HttpRequestMessage request, BrokerSnapshot model)
        {
            var asked = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var selector = JsonDocument.Parse(asked);
            var rootedAt = selector.RootElement.TryGetProperty("traverse", out var traverse)
                           && traverse.ValueKind == JsonValueKind.Array && traverse.GetArrayLength() > 0
                ? Guid.Parse(selector.RootElement.GetProperty("ids")[0].GetString()!)
                : (Guid?)null;
            return rootedAt is { } site ? model.OpenedReaching(site) : model.Opened();
        }
    }

    private static (IntakeWebApplicationFactory Factory, Broker Broker) Holding(BrokerSnapshot? model = null)
    {
        var broker = new Broker();
        var snapshot = model ?? ModelWithTiles();
        return (new IntakeWebApplicationFactory { HandlerCallback = request => broker.Answer(request, snapshot) }, broker);
    }

    private static async Task<string> TicketFor(IntakeWebApplicationFactory factory, HttpClient client, string emailAddress)
    {
        (await client.PostAsJsonAsync("/submissions/verification", new { emailAddress })).EnsureSuccessStatusCode();
        var exchanged = await client.PostAsJsonAsync(
            "/submissions/ticket", new { emailAddress, code = factory.Mailer.CodeSentTo(emailAddress) });
        exchanged.EnsureSuccessStatusCode();
        return (await exchanged.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ticket").GetString()!;
    }

    private static readonly object TheQuestion = new
    {
        property = "temperatureCelsius",
        windowSeconds = 31536000,
        steps = new[] { new { fold = "day", function = "Max" }, new { fold = "monthOfYear", function = "Average" } },
    };

    private static async Task<HttpResponseMessage> ReduceAsync(HttpClient client, string submissionId, string? ticket)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/findings/{submissionId}/reduce")
        {
            Content = JsonContent.Create(TheQuestion),
        };
        if (ticket is not null) request.Headers.Add(TicketHeader, ticket);
        return await client.SendAsync(request);
    }

    // ---- the reduction ---------------------------------------------------------------------------

    [Fact]
    public async Task A_submitter_reduces_a_series_of_their_own_site_and_the_service_names_the_site()
    {
        var (factory, broker) = Holding();
        await using var _ = factory;
        using var client = factory.CreateClient();
        var ticket = await TicketFor(factory, client, ThisAddress);

        var response = await ReduceAsync(client, WillowBend.SubmissionId, ticket);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be(TheReducedAnswer);
        var asked = JsonDocument.Parse(broker.ReduceCalls.Single()).RootElement;
        asked.GetProperty("thingId").GetGuid().Should().Be(ThisSite);
        asked.GetProperty("property").GetString().Should().Be("temperatureCelsius");
        asked.GetProperty("steps").GetArrayLength().Should().Be(2);
        response.Headers.GetValues(TicketHeader).Single().Should().NotBeNullOrEmpty("every act with a live ticket hands a fresh one back");
    }

    [Fact]
    public async Task A_question_the_platform_refuses_is_answered_in_the_platforms_words()
    {
        var (factory, broker) = Holding();
        broker.ReduceAnswer = HttpStatusCode.BadRequest;
        broker.ReduceBody = """{"error":"Unknown fold 'week'."}""";
        await using var _ = factory;
        using var client = factory.CreateClient();

        var response = await ReduceAsync(client, WillowBend.SubmissionId, await TicketFor(factory, client, ThisAddress));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Unknown fold");
    }

    // Valid JSON that is not an object has no fields to forward; it is refused like a body that is not
    // JSON at all, rather than failing inside the service.
    [Fact]
    public async Task A_question_that_is_not_an_object_is_refused_before_the_platform_is_asked()
    {
        var (factory, broker) = Holding();
        await using var _ = factory;
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/findings/{WillowBend.SubmissionId}/reduce")
        {
            Content = JsonContent.Create(new[] { "day", "Max" }),
        };
        request.Headers.Add(TicketHeader, await TicketFor(factory, client, ThisAddress));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        broker.ReduceCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_a_ticket_nothing_is_reduced()
    {
        var (factory, broker) = Holding();
        await using var _ = factory;
        using var client = factory.CreateClient();

        var response = await ReduceAsync(client, WillowBend.SubmissionId, null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        broker.ReduceCalls.Should().BeEmpty();
    }

    // The ticket proves a mailbox and the reference names a submission; a ticket for another mailbox reads
    // nothing of this submission's land, in the one wording the findings use.
    [Fact]
    public async Task A_ticket_for_another_address_reduces_nothing_and_is_told_the_same_as_a_stranger()
    {
        var (factory, broker) = Holding();
        await using var _ = factory;
        using var client = factory.CreateClient();

        var response = await ReduceAsync(client, WillowBend.SubmissionId, await TicketFor(factory, client, OtherAddress));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain(SubmissionFindingsService.NotYourSubmission);
        broker.ReduceCalls.Should().BeEmpty();
    }

    // ---- the tiles -------------------------------------------------------------------------------

    [Fact]
    public async Task A_tile_is_fetched_through_the_registration_the_route_names_and_served_with_its_cache_life()
    {
        var (factory, broker) = Holding();
        await using var _ = factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/basemaps/satellite-tiles/3/4/5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(SomeTileBytes);
        response.Headers.CacheControl!.Public.Should().BeTrue();
        response.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromSeconds(86400));
        var asked = JsonDocument.Parse(broker.EndpointCalls.Single()).RootElement;
        asked.GetProperty("endpointName").GetString().Should().Be("satellite-tiles");
        var parameters = asked.GetProperty("addressParameters");
        (parameters.GetProperty("z").GetString(), parameters.GetProperty("x").GetString(), parameters.GetProperty("y").GetString())
            .Should().Be(("3", "4", "5"));
    }

    // Only a registration the model marks as a basemap's tiles is served this way: the route names a
    // registration, and naming any other would make this service a public proxy for every provider the
    // catalogue registers.
    [Fact]
    public async Task A_registration_not_marked_as_a_basemaps_tiles_is_not_served()
    {
        var (factory, broker) = Holding();
        await using var _ = factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/basemaps/elevation/3/4/5");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        broker.EndpointCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_tile_the_provider_does_not_answer_is_a_503_not_a_broken_image()
    {
        var (factory, broker) = Holding();
        broker.EndpointAnswer = HttpStatusCode.BadGateway;
        await using var _ = factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/basemaps/satellite-tiles/3/4/5");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
