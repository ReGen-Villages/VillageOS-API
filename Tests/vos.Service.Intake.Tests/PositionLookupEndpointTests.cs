using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using vos.Service.Intake.Models;
using vos.Service.Intake.Services;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.Service.Intake.Tests;

// The two lookups a page makes before any site exists: the legal parcel at a clicked
// position, and place names for what somebody typed. Both are anonymous for the reason the form route
// is: the click is the first act, and there is nothing yet to verify against. The outbound call goes
// through the broker's endpoint-forward route, so what these tests fake at the handler is the broker
// answering with what the forwarded fetching service returned — the provider's raw body, because the
// registration carries no reshape for it to apply.
public class PositionLookupEndpointTests
{
    private const string EndpointForwardPath = "/api/endpoints/tributary";

    // What the fixture register answers at the covered position: a ring the registration's own
    // reshape ({"boundary": ring}) turns into the boundary the caller reads.
    private const string ARingOfCorners =
        """
        {"ring": [
          {"latitude": 48.801, "longitude": 2.301},
          {"latitude": 48.802, "longitude": 2.301},
          {"latitude": 48.802, "longitude": 2.303}]}
        """;

    private const string SomePlaces =
        """
        {"found": [
          {"name": "Santarém, Portugal", "latitude": 39.2362, "longitude": -8.6851},
          {"name": "Santarém, Pará, Brazil", "latitude": -2.4431, "longitude": -54.7083}]}
        """;

    private static IntakeWebApplicationFactory Answering(
        DeclaredModel? model = null, string? providerBody = null,
        HttpStatusCode endpointAnswer = HttpStatusCode.OK, List<string>? endpointCalls = null) => new()
    {
        HandlerCallback = request =>
        {
            if (request.RequestUri!.AbsolutePath == EndpointForwardPath)
            {
                endpointCalls?.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                return endpointAnswer == HttpStatusCode.OK && providerBody is not null
                    ? ModelStub.Json(providerBody)
                    : new HttpResponseMessage(endpointAnswer);
            }
            return ModelStub.Json(JsonSerializer.Serialize(
                new SnapshotReadResult(0, (model ?? DeclaredModel.Seeded()).Build()),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        },
    };

    private static Task<HttpResponseMessage> AskForParcelAsync(
        HttpClient client, double latitude, double longitude) =>
        client.PostAsJsonAsync("/submissions/parcel-at-position", new { latitude, longitude });

    [Fact]
    public async Task A_click_inside_a_registers_bounds_answers_the_parcel_as_named_corners()
    {
        var calls = new List<string>();
        await using var factory = Answering(providerBody: ARingOfCorners, endpointCalls: calls);
        using var client = factory.CreateClient();

        var response = await AskForParcelAsync(
            client, WillowBend.CoveredLatitude, WillowBend.CoveredLongitude);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var answered = await response.Content.ReadFromJsonAsync<JsonElement>();
        var corners = answered.GetProperty("boundary").EnumerateArray().ToArray();
        corners.Should().HaveCount(3);
        corners[0].GetProperty("latitude").GetDouble().Should().Be(48.801);
        corners[0].GetProperty("longitude").GetDouble().Should().Be(2.301);
        answered.GetProperty("attribution").GetString()
            .Should().Be(WillowBend.ParcelRegisterAttribution);

        calls.Should().ContainSingle()
            .Which.Should().Contain(WillowBend.ParcelRegisterName, "the register is called by its name")
            .And.Contain("48.8", "the call carries the clicked position for the address placeholders");
    }

    // The Willow Bend site itself sits outside the fixture register's bounds, which is exactly the
    // situation of a neighbouring register: nothing is asked of any provider, and the caller is told to
    // draw — in the same words as a deployment registering no lookup at all, because the caller does
    // the same thing in both.
    [Fact]
    public async Task A_click_outside_every_registers_bounds_is_answered_without_asking_any_provider()
    {
        var calls = new List<string>();
        await using var factory = Answering(providerBody: ARingOfCorners, endpointCalls: calls);
        using var client = factory.CreateClient();

        var response = await AskForParcelAsync(client, 39.5012, -8.4137);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync())
            .Should().Contain(PositionLookupService.NoParcelAvailable);
        calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_model_registering_no_parcel_lookup_answers_the_same_wording()
    {
        await using var factory = Answering(DeclaredModel.Seeded().Without(WillowBend.ParcelRegisterName));
        using var client = factory.CreateClient();

        var response = await AskForParcelAsync(
            client, WillowBend.CoveredLatitude, WillowBend.CoveredLongitude);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync())
            .Should().Contain(PositionLookupService.NoParcelAvailable);
    }

    [Fact]
    public async Task A_position_the_register_holds_no_parcel_for_answers_the_same_wording()
    {
        await using var factory = Answering(providerBody: "{}");
        using var client = factory.CreateClient();

        var response = await AskForParcelAsync(
            client, WillowBend.CoveredLatitude, WillowBend.CoveredLongitude);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync())
            .Should().Contain(PositionLookupService.NoParcelAvailable);
    }

    // A register answering a shape this service would refuse as a submission — here a corner past the
    // pole — is a registration to fix, not a boundary to pass along.
    [Fact]
    public async Task A_register_answering_corners_no_parcel_could_have_answers_no_boundary()
    {
        await using var factory = Answering(providerBody:
            """
            {"ring": [{"latitude": 95.0, "longitude": 2.3},
              {"latitude": 48.8, "longitude": 2.3}, {"latitude": 48.8, "longitude": 2.4}]}
            """);
        using var client = factory.CreateClient();

        var response = await AskForParcelAsync(
            client, WillowBend.CoveredLatitude, WillowBend.CoveredLongitude);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_register_that_cannot_be_asked_is_the_deployments_problem_not_the_positions()
    {
        await using var factory = Answering(endpointAnswer: HttpStatusCode.InternalServerError);
        using var client = factory.CreateClient();

        var response = await AskForParcelAsync(
            client, WillowBend.CoveredLatitude, WillowBend.CoveredLongitude);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"latitude": 48.8}""")]
    [InlineData("""{"latitude": 91.0, "longitude": 2.3}""")]
    [InlineData("""{"latitude": 48.8, "longitude": 181.0}""")]
    public async Task A_missing_or_impossible_position_is_refused_naming_what_is_needed(string body)
    {
        await using var factory = Answering(providerBody: ARingOfCorners);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/submissions/parcel-at-position",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("latitude");
    }

    [Fact]
    public async Task A_typed_query_answers_the_places_the_gazetteer_found()
    {
        await using var factory = Answering(providerBody: SomePlaces);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/submissions/place-search", new { query = "Santarém" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var answered = await response.Content.ReadFromJsonAsync<JsonElement>();
        var places = answered.GetProperty("places").EnumerateArray().ToArray();
        places.Should().HaveCount(2);
        places[0].GetProperty("name").GetString().Should().Be("Santarém, Portugal");
        places[0].GetProperty("latitude").GetDouble().Should().Be(39.2362);
        answered.GetProperty("attribution").GetString().Should().Be(WillowBend.PlaceSearchAttribution);
    }

    private const string FirstSearch = "first-place-search";
    private const string FallbackPredicate = "triedNext";

    private static DeclaredModel SearchingFirstThen() =>
        DeclaredModel.Seeded()
            .With(FallbackPredicate)
            .Bound(PositionLookupReader.FallsBackToRole, FallbackPredicate)
            .Stating(FirstSearch,
                (PositionLookupReader.TransformProperty, WillowBend.PlaceSearchTransform),
                (PositionLookupReader.AttributionProperty, "© the first gazetteer"))
            .Unbound(PositionLookupReader.PlaceSearchRole)
            .Bound(PositionLookupReader.PlaceSearchRole, FirstSearch)
            .Relate(FirstSearch, FallbackPredicate, WillowBend.PlaceSearchName);

    // Each search answers by its own name, so a test can tell which one the query reached.
    private static IntakeWebApplicationFactory AnsweringPerSearch(
        DeclaredModel model, Func<string, HttpResponseMessage> answerFor, List<string> asked) => new()
    {
        HandlerCallback = request =>
        {
            if (request.RequestUri!.AbsolutePath != EndpointForwardPath)
                return ModelStub.Json(JsonSerializer.Serialize(
                    new SnapshotReadResult(0, model.Build()), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var name = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult())
                .RootElement.GetProperty("endpointName").GetString()!;
            asked.Add(name);
            return answerFor(name);
        },
    };

    [Fact]
    public async Task A_refused_search_hands_the_query_to_the_one_it_falls_back_to()
    {
        var asked = new List<string>();
        await using var factory = AnsweringPerSearch(SearchingFirstThen(),
            name => name == FirstSearch ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : ModelStub.Json(SomePlaces),
            asked);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/submissions/place-search", new { query = "Santarém" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        asked.Should().Equal(FirstSearch, WillowBend.PlaceSearchName);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("attribution").GetString().Should().Be(WillowBend.PlaceSearchAttribution);
    }

    [Fact]
    public async Task A_search_that_finds_nothing_hands_the_query_on_and_one_that_finds_places_ends_the_order()
    {
        var asked = new List<string>();
        await using var factory = AnsweringPerSearch(SearchingFirstThen(),
            name => ModelStub.Json(name == FirstSearch ? """{"found": []}""" : SomePlaces),
            asked);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/submissions/place-search", new { query = "Santarém" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        asked.Should().Equal(FirstSearch, WillowBend.PlaceSearchName);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("places").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Finding_nothing_in_every_search_is_an_answer_not_an_outage()
    {
        var asked = new List<string>();
        await using var factory = AnsweringPerSearch(SearchingFirstThen(), _ => ModelStub.Json("""{"found": []}"""), asked);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/submissions/place-search", new { query = "Nowhere at all" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("places").GetArrayLength().Should().Be(0);
    }

    private const string BorderLookup = "border-elevation";
    private const string CentreLookup = "centre-elevation";
    private const string ElevationTransform =
        "{\"elevationMetres\": centre, \"elevationMinimumMetres\": low, \"elevationMaximumMetres\": high}";

    private static DeclaredModel ElevationFirstThenCentre() =>
        DeclaredModel.Seeded()
            .With(FallbackPredicate)
            .Bound(PositionLookupReader.FallsBackToRole, FallbackPredicate)
            .Stating(BorderLookup,
                (PositionLookupReader.TransformProperty, ElevationTransform),
                (PositionLookupReader.AttributionProperty, "© the border heights"))
            .Stating(CentreLookup,
                (PositionLookupReader.TransformProperty, ElevationTransform),
                (PositionLookupReader.AttributionProperty, "© the centre height"))
            .Bound(PositionLookupReader.BorderElevationRole, BorderLookup)
            .Relate(BorderLookup, FallbackPredicate, CentreLookup);

    private static readonly object ASquareBorder = new
    {
        boundary = new[]
        {
            new { latitude = 25.00, longitude = 55.40 }, new { latitude = 25.00, longitude = 55.44 },
            new { latitude = 25.04, longitude = 55.44 }, new { latitude = 25.04, longitude = 55.40 },
        },
    };

    [Fact]
    public async Task A_finished_border_answers_its_centre_and_its_lowest_and_highest_point()
    {
        var asked = new List<string>();
        var sent = new List<string>();
        await using var factory = new IntakeWebApplicationFactory
        {
            HandlerCallback = request =>
            {
                if (request.RequestUri!.AbsolutePath != EndpointForwardPath)
                    return ModelStub.Json(JsonSerializer.Serialize(new SnapshotReadResult(0, ElevationFirstThenCentre().Build()),
                        new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                sent.Add(body);
                asked.Add(JsonDocument.Parse(body).RootElement.GetProperty("endpointName").GetString()!);
                return ModelStub.Json("""{"centre": 100, "low": 84, "high": 112}""");
            },
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/submissions/border-elevation", ASquareBorder);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var answered = await response.Content.ReadFromJsonAsync<JsonElement>();
        answered.GetProperty("elevationMetres").GetDouble().Should().Be(100);
        answered.GetProperty("elevationMinimumMetres").GetDouble().Should().Be(84);
        answered.GetProperty("elevationMaximumMetres").GetDouble().Should().Be(112);
        answered.GetProperty("attribution").GetString().Should().Be("© the border heights");
        asked.Should().Equal(BorderLookup);
        JsonDocument.Parse(sent.Single()).RootElement.GetProperty("addressParameters").GetProperty("locations").GetString()
            .Should().Be("25.02,55.42|25,55.4|25,55.44|25.04,55.44|25.04,55.4");
    }

    [Fact]
    public async Task A_border_lookup_that_is_refused_hands_the_border_to_the_centre_lookup()
    {
        var asked = new List<string>();
        await using var factory = AnsweringPerSearch(ElevationFirstThenCentre(),
            name => name == BorderLookup
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                : ModelStub.Json("""{"centre": 37}"""),
            asked);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/submissions/border-elevation", ASquareBorder);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        asked.Should().Equal(BorderLookup, CentreLookup);
        var answered = await response.Content.ReadFromJsonAsync<JsonElement>();
        answered.GetProperty("elevationMetres").GetDouble().Should().Be(37);
        answered.GetProperty("elevationMinimumMetres").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_model_declaring_no_elevation_lookup_answers_that_none_is_available()
    {
        await using var factory = Answering(providerBody: """{"centre": 100}""");
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/submissions/border-elevation", ASquareBorder);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await RefusalReading.ReadAsync(response)).Code.Should().Be(RefusalCode.NoElevation);
    }

    [Theory]
    [InlineData("""{"boundary": [{"latitude": 25, "longitude": 55}, {"latitude": 25.1, "longitude": 55}]}""", "boundaryTooFewCorners")]
    [InlineData("""{"boundary": [{"latitude": 95, "longitude": 55}, {"latitude": 25.1, "longitude": 55}, {"latitude": 25.1, "longitude": 55.1}]}""", "fieldOutOfRange")]
    [InlineData("""{}""", "boundaryTooFewCorners")]
    public async Task A_border_a_submission_would_refuse_is_refused_before_any_provider_is_asked(string body, string code)
    {
        var calls = new List<string>();
        await using var factory = Answering(ElevationFirstThenCentre(), providerBody: """{"centre": 1}""", endpointCalls: calls);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/submissions/border-elevation",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RefusalReading.ReadAsync(response)).Code.Should().Be(code);
        calls.Should().BeEmpty();
    }

    [Fact]
    public void A_border_of_more_corners_than_the_provider_takes_is_sampled_evenly_round_it()
    {
        var border = Enumerable.Range(0, 300).Select(index => new BoundaryPoint { Latitude = index, Longitude = 0 }).ToList();

        var sampled = PositionLookupService.SampledCorners(border);

        sampled.Should().HaveCount(PositionLookupService.MostElevationCorners);
        sampled.Select(point => point.Latitude).Should().BeInAscendingOrder().And.Contain(0);
        sampled[^1].Latitude.Should().BeGreaterThan(290);
    }

    [Fact]
    public async Task A_model_registering_no_place_search_says_so()
    {
        await using var factory = Answering(DeclaredModel.Seeded().Without(WillowBend.PlaceSearchName));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/submissions/place-search", new { query = "Santarém" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync())
            .Should().Contain(PositionLookupService.NoSearchAvailable);
    }

    // A reshape the model declares but the engine cannot run is a registration to fix, answered the way
    // an unseeded model is: 503 with nothing about the model in the body, and the fault in the log.
    [Theory]
    [InlineData("/submissions/parcel-at-position", """{"latitude": 48.8, "longitude": 2.3}""")]
    [InlineData("/submissions/place-search", """{"query": "Santarém"}""")]
    public async Task A_reshape_that_cannot_be_applied_is_not_described_to_the_caller(
        string route, string body)
    {
        var model = DeclaredModel.Seeded()
            .Stating(WillowBend.ParcelRegisterName,
                (PositionLookupReader.TransformProperty, "this is ((( not JSONata"),
                (PositionLookupReader.SouthLatitudeProperty, 41.0),
                (PositionLookupReader.NorthLatitudeProperty, 51.5),
                (PositionLookupReader.WestLongitudeProperty, -5.5),
                (PositionLookupReader.EastLongitudeProperty, 10.0))
            .Stating(WillowBend.PlaceSearchName,
                (PositionLookupReader.TransformProperty, "this is ((( not JSONata"));
        await using var factory = Answering(model, providerBody: "{}");
        using var client = factory.CreateClient();

        var response = await client.PostAsync(route,
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("JSONata");
    }

    [Fact]
    public async Task A_gazetteer_that_cannot_be_asked_is_the_deployments_problem_not_the_querys()
    {
        await using var factory = Answering(endpointAnswer: HttpStatusCode.InternalServerError);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/submissions/place-search", new { query = "Santarém" });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    // The broker itself refusing the model read is not the stub above answering a provider failure:
    // the read throws rather than answering null, and the route still owes the stranger a bare 503.
    [Fact]
    public async Task A_broker_that_cannot_be_read_is_answered_as_an_unseeded_model_is()
    {
        await using var factory = new IntakeWebApplicationFactory
        {
            HandlerCallback = request =>
                request.RequestUri!.AbsolutePath == EndpointForwardPath
                    ? ModelStub.Json(SomePlaces)
                    : new HttpResponseMessage(HttpStatusCode.InternalServerError),
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/submissions/place-search", new { query = "Santarém" });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    // A connection that dies mid-call surfaces as an exception rather than a status; the lookup treats
    // it as the provider being unaskable, never as the caller's fault.
    [Fact]
    public async Task A_connection_that_fails_mid_call_reads_as_the_provider_being_unaskable()
    {
        await using var factory = new IntakeWebApplicationFactory
        {
            HandlerCallback = request =>
                request.RequestUri!.AbsolutePath == EndpointForwardPath
                    ? throw new HttpRequestException("the connection was torn down")
                    : ModelStub.Json(JsonSerializer.Serialize(
                        new SnapshotReadResult(0, DeclaredModel.Seeded().Build()),
                        new JsonSerializerOptions(JsonSerializerDefaults.Web))),
        };
        using var client = factory.CreateClient();

        var response = await AskForParcelAsync(
            client, WillowBend.CoveredLatitude, WillowBend.CoveredLongitude);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    // A gazetteer's answer is read entry by entry: one it garbles is dropped rather than dragging the
    // rest down, and an answer shaped as no list at all reads as no places found.
    [Theory]
    [InlineData(
        """
        {"found": [
          {"name": "Santarém, Portugal", "latitude": 39.2362, "longitude": -8.6851},
          {"latitude": 1.0, "longitude": 1.0},
          {"name": "Nowhere", "latitude": 95.0, "longitude": 1.0}]}
        """, 1)]
    [InlineData("""{"answer": 42}""", 0)]
    public async Task A_garbled_gazetteer_answer_keeps_only_the_places_a_pin_could_land_on(
        string providerBody, int kept)
    {
        await using var factory = Answering(providerBody: providerBody);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/submissions/place-search", new { query = "Santarém" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var answered = await response.Content.ReadFromJsonAsync<JsonElement>();
        answered.GetProperty("places").GetArrayLength().Should().Be(kept);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"query": "   "}""")]
    public async Task A_search_with_nothing_to_search_for_is_refused(string body)
    {
        await using var factory = Answering(providerBody: SomePlaces);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/submissions/place-search",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("query");
    }
}
