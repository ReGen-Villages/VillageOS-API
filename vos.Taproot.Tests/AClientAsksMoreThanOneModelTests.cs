using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Taproot.Tests;

[Collection(nameof(CliEnvVarCollection))]
public class AClientAsksMoreThanOneModelTests
{
    private const string MyceliumUrl = "https://localhost:7243";

    private static readonly Guid Hillside = Guid.Parse("7a1f0f5e-52a4-4c7b-9d0a-3f1f3c1f8a10");
    private static readonly Guid Riverbend = Guid.Parse("2c9d6b1e-8f0a-4d67-b1a3-6e5f0a9c4d22");

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static MockHttpMessageHandler AMyceliumWhoseTokensNameTheModelAskedFor() => new(request =>
        request.RequestUri!.AbsolutePath == "/api/auth/token"
            ? Json($$"""{"token":"token{{request.RequestUri.Query}}"}""")
            : Json("[]"));

    private static IEnumerable<string?> TokensOnRequestsFor(MockHttpMessageHandler mycelium, string path) =>
        mycelium.Requests
            .Where(request => request.RequestUri!.AbsolutePath == path)
            .Select(request => request.Headers.Authorization?.Parameter);

    [Fact]
    public async Task A_client_for_a_model_exchanges_the_key_for_that_model()
    {
        var mycelium = AMyceliumWhoseTokensNameTheModelAskedFor();
        var client = new MyceliumClient(MyceliumUrl, "an-api-key", mycelium);

        await client.ForModel(Hillside).GetAllThingsAsync();
        await client.GetAllThingsAsync();

        mycelium.Requests.Where(request => request.RequestUri!.AbsolutePath == "/api/auth/token")
            .Select(request => request.RequestUri!.Query)
            .Should().Equal($"?modelId={Hillside}", "");
    }

    [Fact]
    public async Task Calls_for_different_models_made_at_the_same_time_each_carry_their_own_models_token()
    {
        var mycelium = AMyceliumWhoseTokensNameTheModelAskedFor();
        var client = new MyceliumClient(MyceliumUrl, "an-api-key", mycelium);
        var forHillside = client.ForModel(Hillside);
        var forRiverbend = client.ForModel(Riverbend);

        await Task.WhenAll(Enumerable.Range(0, 50).SelectMany(_ => new[]
        {
            Task.Run(forHillside.GetAllThingsAsync),
            Task.Run(forRiverbend.GetAllRelationshipsAsync),
        }));

        TokensOnRequestsFor(mycelium, "/api/things").Should().OnlyContain(token => token == $"token?modelId={Hillside}");
        TokensOnRequestsFor(mycelium, "/api/relationships").Should().OnlyContain(token => token == $"token?modelId={Riverbend}");
    }

    [Fact]
    public async Task A_model_the_key_may_not_enter_is_refused_in_the_platforms_words()
    {
        var mycelium = new MockHttpMessageHandler(_ => Json("""{"error":"This key is confined to another model"}""", HttpStatusCode.Forbidden));
        var client = new MyceliumClient(MyceliumUrl, "an-api-key", mycelium);

        var asking = () => client.ForModel(Hillside).GetAllThingsAsync();

        (await asking.Should().ThrowAsync<KeyRefusedException>())
            .Which.Message.Should().Contain("This key is confined to another model");
    }

    [Fact]
    public async Task A_client_for_a_model_names_the_same_program()
    {
        var mycelium = AMyceliumWhoseTokensNameTheModelAskedFor();
        var client = new MyceliumClient(MyceliumUrl, "an-api-key", mycelium, programName: "Another program");

        await client.ForModel(Hillside).GetAllThingsAsync();

        mycelium.Requests.Should().OnlyContain(request =>
            request.Headers.GetValues(MyceliumClient.ProgramHeader).Single() == "Another program");
    }

    private static async Task<HttpRequestMessage> SentThroughTheTokenStepAsync(HttpRequestMessage request)
    {
        var transport = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var sender = new HttpMessageInvoker(
            new TokenAttachingHandler(() => Task.FromResult("the-token"), new Uri(MyceliumUrl), transport));

        await sender.SendAsync(request, CancellationToken.None);
        return transport.Requests.Single();
    }

    [Fact]
    public async Task The_token_is_attached_to_a_request_for_Mycelium() =>
        (await SentThroughTheTokenStepAsync(new HttpRequestMessage(HttpMethod.Get, $"{MyceliumUrl}/api/things")))
            .Headers.Authorization.Should().Be(new AuthenticationHeaderValue("Bearer", "the-token"));

    [Theory]
    [InlineData("https://localhost:6100/ingest")]
    [InlineData("http://localhost:7243/api/things")]
    [InlineData("https://elsewhere.example/api/things")]
    public async Task The_token_is_never_attached_to_a_request_for_another_address(string address) =>
        (await SentThroughTheTokenStepAsync(new HttpRequestMessage(HttpMethod.Get, address)))
            .Headers.Authorization.Should().BeNull();

    [Fact]
    public async Task The_exchange_of_the_key_carries_no_token()
    {
        var exchange = new HttpRequestMessage(HttpMethod.Post, $"{MyceliumUrl}/api/auth/token");
        exchange.Headers.Add(MyceliumClient.ApiKeyHeader, "an-api-key");

        (await SentThroughTheTokenStepAsync(exchange)).Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task A_request_that_states_its_own_credential_keeps_it()
    {
        var stated = new HttpRequestMessage(HttpMethod.Get, $"{MyceliumUrl}/api/logs/stream");
        stated.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "its-own");

        (await SentThroughTheTokenStepAsync(stated)).Headers.Authorization!.Parameter.Should().Be("its-own");
    }
}
