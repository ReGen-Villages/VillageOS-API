using System.Net;
using System.Text;
using FluentAssertions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Taproot.Tests;

[Collection(nameof(CliEnvVarCollection))]
public class AThingIsFoundByNameAndARouteIsAskedByPathTests
{
    private const string MyceliumUrl = "https://localhost:7243";

    private static readonly Guid Spring = Guid.Parse("0d6c1a0e-9f55-4a0e-8a59-0d3e7b0c2f44");
    private static readonly Guid AnotherSpring = Guid.Parse("5b8e2d1c-7a44-4f0b-9c3d-1e2f3a4b5c66");

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (NameResolver Lookup, MyceliumClient Client, MockHttpMessageHandler Mycelium) AModelAnswering(
        Func<HttpRequestMessage, HttpResponseMessage> answer)
    {
        var mycelium = new MockHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath == "/api/auth/token" ? Json("""{"token":"a-token"}""") : answer(request));
        var client = new MyceliumClient(MyceliumUrl, "an-api-key", mycelium);
        return (new NameResolver(client), client, mycelium);
    }

    private static IEnumerable<HttpRequestMessage> AskedOf(MockHttpMessageHandler mycelium) =>
        mycelium.Requests.Where(request => request.RequestUri!.AbsolutePath != "/api/auth/token");

    [Fact]
    public async Task A_name_one_Thing_holds_is_found_by_asking_for_that_name_alone()
    {
        var (lookup, _, mycelium) = AModelAnswering(_ => Json($$"""{"Id":"{{Spring}}","Name":"North spring"}"""));

        var found = await lookup.FindThingAsync("North spring");

        found.IsSuccess.Should().BeTrue();
        found.Id.Should().Be(Spring);
        AskedOf(mycelium).Should().ContainSingle()
            .Which.RequestUri!.PathAndQuery.Should().Be("/api/things?name=North%20spring");
    }

    [Fact]
    public async Task A_name_no_Thing_holds_is_answered_as_not_found()
    {
        var (lookup, _, _) = AModelAnswering(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var found = await lookup.FindThingAsync("North spring");

        found.IsSuccess.Should().BeFalse();
        found.ErrorMessage.Should().Be("No thing found with name 'North spring'.");
    }

    [Fact]
    public async Task A_name_more_than_one_Thing_holds_is_answered_with_the_identifiers_to_choose_from()
    {
        var (lookup, _, _) = AModelAnswering(_ => Json(
            $$"""{"error":"More than one Thing is named 'North spring'","matchingIds":["{{Spring}}","{{AnotherSpring}}"]}""",
            HttpStatusCode.Conflict));

        var found = await lookup.FindThingAsync("North spring");

        found.IsSuccess.Should().BeFalse();
        found.ErrorMessage.Should().Contain("Ambiguous name 'North spring'")
            .And.Contain(Spring.ToString()).And.Contain(AnotherSpring.ToString());
    }

    [Fact]
    public async Task A_name_holding_a_comma_is_answered_without_asking_the_platform()
    {
        var (lookup, _, mycelium) = AModelAnswering(_ => Json("[]"));

        var found = await lookup.FindThingAsync("Spring, north");

        found.IsSuccess.Should().BeFalse();
        found.ErrorMessage.Should().Contain("comma").And.Contain("identifier");
        AskedOf(mycelium).Should().BeEmpty();
    }

    [Fact]
    public async Task An_identifier_is_taken_as_it_is_without_asking_the_platform()
    {
        var (lookup, _, mycelium) = AModelAnswering(_ => Json("[]"));

        (await lookup.FindThingAsync(Spring.ToString())).Id.Should().Be(Spring);
        mycelium.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_lookup_the_platform_refuses_for_another_reason_keeps_that_reason()
    {
        var (lookup, _, _) = AModelAnswering(_ => Json("""{"error":"The model is still loading"}""", HttpStatusCode.ServiceUnavailable));

        var asking = () => lookup.FindThingAsync("North spring");

        (await asking.Should().ThrowAsync<HttpRequestException>()).Which.Message.Should().Contain("The model is still loading");
    }

    [Fact]
    public async Task A_selection_is_read_once_by_posting_the_selector_and_answers_what_the_platform_selected()
    {
        string? sent = null;
        var (_, client, mycelium) = AModelAnswering(request =>
        {
            sent = request.Content!.ReadAsStringAsync().Result;
            return Json("""{"watermark":7,"snapshot":{"things":[],"relationships":[]}}""");
        });
        var selector = new System.Text.Json.Nodes.JsonObject
        {
            ["ids"] = new System.Text.Json.Nodes.JsonArray(Spring.ToString()),
            ["includeIsAncestors"] = false,
        };

        var answer = await client.ReadSelectionAsync(selector);

        answer.GetProperty("watermark").GetInt32().Should().Be(7);
        var asked = AskedOf(mycelium).Should().ContainSingle().Subject;
        asked.Method.Should().Be(HttpMethod.Post);
        asked.RequestUri!.PathAndQuery.Should().Be("/api/snapshots");
        asked.Headers.Authorization!.Parameter.Should().Be("a-token");
        System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(sent!), selector).Should().BeTrue();
    }

    [Fact]
    public async Task A_name_in_a_selector_holding_a_quote_a_backslash_and_a_brace_is_sent_as_that_one_name()
    {
        const string name = """Spring", "all": true} \ {""";
        string? sent = null;
        var (_, client, _) = AModelAnswering(request =>
        {
            sent = request.Content!.ReadAsStringAsync().Result;
            return Json("{}");
        });

        await client.ReadSelectionAsync(new System.Text.Json.Nodes.JsonObject
        {
            ["names"] = new System.Text.Json.Nodes.JsonArray(name),
        });

        var received = System.Text.Json.JsonDocument.Parse(sent!).RootElement;
        received.EnumerateObject().Select(member => member.Name).Should().Equal("names");
        received.GetProperty("names").EnumerateArray().Select(one => one.GetString()).Should().Equal(name);
    }

    [Fact]
    public async Task A_selection_the_platform_refuses_keeps_its_reason()
    {
        var (_, client, _) = AModelAnswering(_ => Json("""{"error":"A selector names nothing to select"}""", HttpStatusCode.BadRequest));

        var asking = () => client.ReadSelectionAsync(new System.Text.Json.Nodes.JsonObject());

        (await asking.Should().ThrowAsync<HttpRequestException>()).Which.Message.Should().Contain("A selector names nothing to select");
    }

    [Fact]
    public async Task A_request_by_path_sends_the_method_the_path_and_the_body_and_answers_the_body()
    {
        var (_, client, mycelium) = AModelAnswering(_ => Json("""{"said":"done"}"""));

        var answer = await client.RequestAsync(HttpMethod.Post, "/api/things/abc/press?step=2", """{"choice":"left"}""");

        answer.Should().Be("""{"said":"done"}""");
        var sent = AskedOf(mycelium).Should().ContainSingle().Subject;
        sent.Method.Should().Be(HttpMethod.Post);
        sent.RequestUri!.PathAndQuery.Should().Be("/api/things/abc/press?step=2");
        (await sent.Content!.ReadAsStringAsync()).Should().Be("""{"choice":"left"}""");
        sent.Headers.Authorization!.Parameter.Should().Be("a-token");
    }

    [Fact]
    public async Task A_request_by_path_with_no_body_sends_none()
    {
        var (_, client, mycelium) = AModelAnswering(_ => Json("[]"));

        await client.RequestAsync(HttpMethod.Get, "/api/models");

        AskedOf(mycelium).Single().Content.Should().BeNull();
    }

    [Fact]
    public async Task A_request_by_path_the_platform_refuses_keeps_its_reason()
    {
        var (_, client, _) = AModelAnswering(_ => Json("""{"error":"That step is not open"}""", HttpStatusCode.BadRequest));

        var asking = () => client.RequestAsync(HttpMethod.Post, "/api/things/abc/press", "{}");

        (await asking.Should().ThrowAsync<HttpRequestException>()).Which.Message.Should().Contain("400").And.Contain("That step is not open");
    }

    [Theory]
    [InlineData("https://elsewhere.example/api/things")]
    [InlineData("api/things")]
    [InlineData("/swagger")]
    public async Task A_path_that_is_not_one_of_the_platforms_routes_is_refused_and_nothing_is_sent(string path)
    {
        var (_, client, mycelium) = AModelAnswering(_ => Json("[]"));

        var asking = () => client.RequestAsync(HttpMethod.Get, path);

        await asking.Should().ThrowAsync<ArgumentException>();
        mycelium.Requests.Should().BeEmpty();
    }
}
