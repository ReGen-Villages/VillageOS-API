using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Shared.Subscriptions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Shared.Tests.Subscriptions;

// The bearer forwarded with a /handle call names the caller's model but expires in minutes, so a
// subscription that has to outlast it trades up. These pin the call that does the trading, and pin that a
// refusal is survivable rather than fatal — a daemon that cannot extend one model's token must keep
// serving the others.
public class ServiceTokenExchangeTests
{
    private const string Url = "http://mycelium";
    private static readonly Guid Model = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class FreshClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FreshClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private static (ServiceTokenExchange Exchange, MockHttpMessageHandler Handler) Build(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new MockHttpMessageHandler(respond);
        return (new ServiceTokenExchange(new FreshClientFactory(handler), NullLogger.Instance, Url), handler);
    }

    private static HttpResponseMessage Issuing(string token) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(new { token }) };

    [Fact]
    public async Task Presents_the_bearer_it_was_given_to_the_service_token_route()
    {
        var (exchange, handler) = Build(_ => Issuing(TestTokens.For(Model)));

        await exchange.ExchangeAsync("the-caller-bearer");

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.AbsolutePath.Should().Be("/api/auth/service-token");
        request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        request.Headers.Authorization.Parameter.Should().Be("the-caller-bearer");
    }

    [Fact]
    public async Task Returns_the_model_and_expiry_of_the_token_mycelium_issued()
    {
        var expires = DateTimeOffset.UtcNow.AddHours(24);
        var (exchange, _) = Build(_ => Issuing(TestTokens.For(Model, expires)));

        var extended = await exchange.ExchangeAsync("the-caller-bearer");

        extended.Should().NotBeNull();
        extended!.ModelId.Should().Be(Model);
        extended.ExpiresAt.Should().BeCloseTo(expires, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_refusal_yields_nothing_rather_than_throwing()
    {
        var (exchange, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        (await exchange.ExchangeAsync("the-caller-bearer")).Should().BeNull();
    }

    [Fact]
    public async Task A_token_that_cannot_be_read_yields_nothing()
    {
        var (exchange, _) = Build(_ => Issuing("not-a-token"));

        (await exchange.ExchangeAsync("the-caller-bearer")).Should().BeNull();
    }

    [Fact]
    public async Task Mycelium_being_unreachable_yields_nothing()
    {
        var (exchange, _) = Build(_ => throw new HttpRequestException("no route to host"));

        (await exchange.ExchangeAsync("the-caller-bearer")).Should().BeNull();
    }
}
