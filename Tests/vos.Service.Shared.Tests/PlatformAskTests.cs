using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Shared.Tests;

public class PlatformAskTests
{
    private const string MyceliumUrl = "http://localhost:7243";

    [Fact]
    public async Task A_name_is_asked_for_escaped_on_the_things_route()
    {
        var (client, handler) = Client(_ => Answer(HttpStatusCode.OK, """{"Id":"11111111-1111-1111-1111-111111111111","Name":"a b"}"""));

        var answer = await client.AskForThingNamedPublicAsync("a b&c");

        handler.Requests.Single().Method.Should().Be(HttpMethod.Get);
        handler.Requests.Single().RequestUri!.PathAndQuery.Should().Be("/api/things?name=a%20b%26c");
        answer.Should().Be(new PlatformAnswer(PlatformOutcome.Done, HttpStatusCode.OK,
            """{"Id":"11111111-1111-1111-1111-111111111111","Name":"a b"}"""));
    }

    [Fact]
    public async Task A_create_posts_the_content_it_is_given_to_the_things_route()
    {
        var (client, handler) = Client(_ => Answer(HttpStatusCode.Created, "{}"));

        await client.AskToCreateThingPublicAsync(JsonContent.Create(new { Name = "spring" }));

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.AbsolutePath.Should().Be("/api/things");
        (await request.Content!.ReadAsStringAsync()).Should().Be("""{"name":"spring"}""");
    }

    [Fact]
    public async Task A_delete_names_the_thing_on_the_things_route()
    {
        var thingId = Guid.NewGuid();
        var (client, handler) = Client(_ => Answer(HttpStatusCode.OK, ""));

        (await client.AskToDeleteThingPublicAsync(thingId)).IsDone.Should().BeTrue();

        handler.Requests.Single().Method.Should().Be(HttpMethod.Delete);
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be($"/api/things/{thingId}");
    }

    [Fact]
    public async Task A_fragment_is_posted_to_the_fragment_route_as_it_is_given()
    {
        var (client, handler) = Client(_ => Answer(HttpStatusCode.OK, "{}"));

        await client.AskToApplyFragmentPublicAsync(new StringContent("""{"Name":"f"}""", Encoding.UTF8, "application/json"));

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.AbsolutePath.Should().Be("/api/model/fragment");
        (await request.Content!.ReadAsStringAsync()).Should().Be("""{"Name":"f"}""");
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, PlatformOutcome.Done)]
    [InlineData(HttpStatusCode.NoContent, PlatformOutcome.Done)]
    [InlineData(HttpStatusCode.NotFound, PlatformOutcome.NotFound)]
    [InlineData(HttpStatusCode.Forbidden, PlatformOutcome.Refused)]
    [InlineData(HttpStatusCode.BadRequest, PlatformOutcome.Refused)]
    [InlineData(HttpStatusCode.ServiceUnavailable, PlatformOutcome.Refused)]
    public async Task An_answer_is_done_not_found_or_refused_by_its_status(HttpStatusCode status, PlatformOutcome outcome)
    {
        var (client, _) = Client(_ => Answer(status, """{"error":"why"}"""));

        var answer = await client.AskToDeleteThingPublicAsync(Guid.NewGuid());

        answer.Outcome.Should().Be(outcome);
        answer.Status.Should().Be(status);
        answer.Body.Should().Be("""{"error":"why"}""");
    }

    [Theory]
    [InlineData("""{"error":"only an admin deletes this"}""", "only an admin deletes this")]
    [InlineData("""{"Error":"only an admin deletes this"}""", "only an admin deletes this")]
    [InlineData("""{"error":403}""", null)]
    [InlineData("""{"title":"Forbidden"}""", null)]
    [InlineData("""["error"]""", null)]
    [InlineData("not json at all", null)]
    [InlineData("", null)]
    public void The_error_words_are_the_error_a_json_answer_states(string body, string? words)
    {
        new PlatformAnswer(PlatformOutcome.Refused, HttpStatusCode.Forbidden, body).ErrorWords().Should().Be(words);
        PlatformAnswer.ErrorWordsIn(body).Should().Be(words);
    }

    [Fact]
    public void An_answer_reads_as_the_json_it_carries()
    {
        var answer = new PlatformAnswer(PlatformOutcome.Done, HttpStatusCode.OK, """{"Name":"spring"}""");

        answer.Json().GetProperty("Name").GetString().Should().Be("spring");
    }

    [Theory]
    [InlineData("""{"Id":"11111111-1111-1111-1111-111111111111"}""", "11111111-1111-1111-1111-111111111111")]
    [InlineData("""{"id":"11111111-1111-1111-1111-111111111111"}""", "11111111-1111-1111-1111-111111111111")]
    [InlineData("""{"Id":"not a guid"}""", null)]
    [InlineData("""{"Id":17}""", null)]
    [InlineData("""{"Name":"is"}""", null)]
    [InlineData("null", null)]
    [InlineData("""[{"Id":"11111111-1111-1111-1111-111111111111"}]""", null)]
    public void A_things_identifier_is_its_id_written_as_a_guid(string thing, string? identifier)
    {
        TestableMyceliumClient.IdentifierOfPublic(JsonDocument.Parse(thing).RootElement)
            .Should().Be(identifier is null ? null : Guid.Parse(identifier));
    }

    [Theory]
    [InlineData("""{"Id":"11111111-1111-1111-1111-111111111111","Name":"spring"}""", true, "spring")]
    [InlineData("""{"id":"11111111-1111-1111-1111-111111111111","name":"spring"}""", true, "spring")]
    [InlineData("""{"Id":"11111111-1111-1111-1111-111111111111"}""", false, "")]
    [InlineData("""{"Id":"11111111-1111-1111-1111-111111111111","Name":7}""", false, "")]
    [InlineData("""{"Name":"spring"}""", false, "")]
    public void A_thing_is_read_only_with_both_its_identifier_and_its_name(string thing, bool read, string name)
    {
        TestableMyceliumClient.TryReadThingPublic(JsonDocument.Parse(thing).RootElement, out var identifier, out var named)
            .Should().Be(read);
        named.Should().Be(name);
        if (read) identifier.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    }

    private static (TestableMyceliumClient Client, MockHttpMessageHandler Handler) Client(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new MockHttpMessageHandler(respond);
        var client = new TestableMyceliumClient(new PerCallHttpClientFactory(handler), NullLogger.Instance, MyceliumUrl,
            new LaunchToken("service-token"));
        return (client, handler);
    }

    private static HttpResponseMessage Answer(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
