using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Shared.Tests;

public class ApiKeyTokenSourceTests
{
    private const string MyceliumUrl = "http://localhost:7243";
    private const string Key = "api-key-abc";

    private static readonly DateTimeOffset Start = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    private static (ApiKeyTokenSource source, MockHttpMessageHandler handler) Build(
        Func<HttpRequestMessage, HttpResponseMessage> respond, Func<DateTimeOffset>? now = null)
    {
        var handler = new MockHttpMessageHandler(respond);
        var factory = new TestHttpClientFactory(new HttpClient(handler));
        return (new ApiKeyTokenSource(factory, NullLogger.Instance, MyceliumUrl, Key, now ?? (() => Start)), handler);
    }

    private static HttpResponseMessage Minted(string token) => new(HttpStatusCode.OK)
    {
        Content = new StringContent($"{{\"token\":\"{token}\"}}", Encoding.UTF8, "application/json")
    };

    [Fact]
    public async Task TheKeyIsSentAsTheHeader_AndTheMintedTokenComesBack()
    {
        var minted = TestTokens.For(Guid.NewGuid(), Start.AddMinutes(5));
        var (source, handler) = Build(request =>
        {
            request.RequestUri!.AbsolutePath.Should().Be("/api/auth/token");
            request.Headers.GetValues("X-API-Key").Should().ContainSingle().Which.Should().Be(Key);
            return Minted(minted);
        });

        (await source.GetTokenAsync()).Should().Be(minted);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task ASecondAskWithinTheTokensLife_DoesNotExchangeAgain()
    {
        var minted = TestTokens.For(Guid.NewGuid(), Start.AddMinutes(5));
        var (source, handler) = Build(_ => Minted(minted));

        await source.GetTokenAsync();
        (await source.GetTokenAsync()).Should().Be(minted);

        handler.Requests.Should().ContainSingle("the held token is still usable, so no second exchange happens");
    }

    [Fact]
    public async Task AnAskCloseToExpiry_ExchangesAgain()
    {
        var now = Start;
        var first = TestTokens.For(Guid.NewGuid(), Start.AddMinutes(5));
        var second = TestTokens.For(Guid.NewGuid(), Start.AddMinutes(10));
        var answers = new Queue<string>([first, second]);
        var (source, handler) = Build(_ => Minted(answers.Dequeue()), () => now);

        await source.GetTokenAsync();
        now = Start.AddMinutes(5) - TimeSpan.FromSeconds(10);

        (await source.GetTokenAsync()).Should().Be(second);
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task ARefusedExchange_AnswersNull()
    {
        var (source, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        (await source.GetTokenAsync()).Should().BeNull();
    }

    [Fact]
    public async Task ATokenStatingNoExpiry_IsNotHeld()
    {
        var unbounded = TestTokens.Jwt(new Dictionary<string, object> { ["vos:scope"] = "endpoint:test:*" });
        var (source, handler) = Build(_ => Minted(unbounded));

        (await source.GetTokenAsync()).Should().BeNull();
        (await source.GetTokenAsync()).Should().BeNull();

        handler.Requests.Should().HaveCount(2, "a token with no stated end is never held, so each ask exchanges anew");
    }

    [Fact]
    public async Task AnUnreachableBroker_AnswersNull()
    {
        var (source, _) = Build(_ => throw new HttpRequestException("mycelium unreachable"));

        (await source.GetTokenAsync()).Should().BeNull();
    }
}
