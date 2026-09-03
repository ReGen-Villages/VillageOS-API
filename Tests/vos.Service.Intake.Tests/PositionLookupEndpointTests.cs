using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using vos.Service.Intake.Services;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.Service.Intake.Tests;

// Feature #6906 — the two lookups a page makes before any site exists: the legal parcel at a clicked
// position, and place names for what somebody typed. Both are anonymous for the reason the form route
// is: the click is the first act, and there is nothing yet to verify against. The outbound call goes
// through the broker's endpoint-forward route, so what these tests fake at the handler is the broker
// answering with what the forwarded fetching service returned — the provider's raw body, because the
// registration carries no reshape for it to apply.
public class PositionLookupEndpointTests
{
    private const string EndpointForwardPath = "/api/endpoints/tributary";

    /// <summary>What the fixture register answers at the covered position: a ring the registration's own
    /// reshape (<c>{"boundary": ring}</c>) turns into the boundary the caller reads.</summary>
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
                new SubscribeResult(Guid.NewGuid(), 0, (model ?? DeclaredModel.Seeded()).Build()),
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
    // situation of a register next door: nothing is asked of any provider, and the caller is told to
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
