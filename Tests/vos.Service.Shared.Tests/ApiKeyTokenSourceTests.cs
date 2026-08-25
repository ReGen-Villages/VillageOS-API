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

    private static HttpResponseMessage JsonAnswer(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Minted(string token) => JsonAnswer($"{{\"token\":\"{token}\"}}");

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
        var now = Start;
        var unbounded = TestTokens.Jwt(new Dictionary<string, object> { ["vos:scope"] = "endpoint:test:*" });
        var (source, handler) = Build(_ => Minted(unbounded), () => now);

        (await source.GetTokenAsync()).Should().BeNull();
        now = Start.AddMinutes(1);
        (await source.GetTokenAsync()).Should().BeNull();

        handler.Requests.Should().HaveCount(2, "a token with no stated end is never held, so a later ask exchanges anew");
    }

    // Asking runs on the caller's thread as far as the wait for the exchange, so the second ask below is
    // queued behind the first by the time it hands a task back — no delay is needed to arrange the race.
    [Fact]
    public async Task ACallerThatWaitedForAnExchange_IsServedByItRatherThanExchangingAgain()
    {
        var minted = TestTokens.For(Guid.NewGuid(), Start.AddMinutes(5));
        var exchangeStarted = new TaskCompletionSource();
        var finishExchange = new TaskCompletionSource();
        var handler = MockHttpMessageHandler.AnsweringAsynchronously(async _ =>
        {
            exchangeStarted.TrySetResult();
            await finishExchange.Task;
            return Minted(minted);
        });
        var source = new ApiKeyTokenSource(
            new TestHttpClientFactory(new HttpClient(handler)), NullLogger.Instance, MyceliumUrl, Key, () => Start);

        var first = source.GetTokenAsync();
        await exchangeStarted.Task;
        var second = source.GetTokenAsync();
        finishExchange.SetResult();

        (await first).Should().Be(minted);
        (await second).Should().Be(minted);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task AnAnswerCarryingNoToken_IsNotHeld()
    {
        var (source, _) = Build(_ => JsonAnswer("{\"issued\":true}"));

        (await source.GetTokenAsync()).Should().BeNull();
    }

    [Fact]
    public async Task AnAnswerWhoseTokenIsNotAJwt_IsNotHeld()
    {
        var (source, _) = Build(_ => Minted("not-a-jwt"));

        (await source.GetTokenAsync()).Should().BeNull();
    }

    [Fact]
    public async Task AnAskCancelledMidExchange_SaysSoRatherThanAnsweringNull()
    {
        using var cancellation = new CancellationTokenSource();
        var exchangeStarted = new TaskCompletionSource();
        var handler = MockHttpMessageHandler.ObservingCancellation(async (_, cancellationToken) =>
        {
            exchangeStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var source = new ApiKeyTokenSource(
            new TestHttpClientFactory(new HttpClient(handler)), NullLogger.Instance, MyceliumUrl, Key, () => Start);

        var ask = source.GetTokenAsync(cancellation.Token);
        await exchangeStarted.Task;
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ask);
    }

    [Fact]
    public async Task AFruitlessExchange_IsNotRepeatedStraightAway()
    {
        var (source, handler) = Build(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        await source.GetTokenAsync();
        (await source.GetTokenAsync()).Should().BeNull();

        handler.Requests.Should().ContainSingle(
            "the refusal is remembered, so callers do not each take a turn at the full exchange timeout");
    }

    [Fact]
    public async Task OnceTheRetryDelayHasPassed_TheKeyIsOfferedAgain()
    {
        var now = Start;
        var (source, handler) = Build(_ => new HttpResponseMessage(HttpStatusCode.Forbidden), () => now);

        await source.GetTokenAsync();
        now = Start.AddSeconds(30);
        await source.GetTokenAsync();

        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task AnExchangeThatSucceedsAfterARefusal_IsHeldLikeAnyOther()
    {
        var now = Start;
        var minted = TestTokens.For(Guid.NewGuid(), Start.AddMinutes(5));
        var refusing = true;
        var (source, handler) = Build(
            _ => refusing ? new HttpResponseMessage(HttpStatusCode.Forbidden) : Minted(minted), () => now);

        await source.GetTokenAsync();
        now = Start.AddSeconds(30);
        refusing = false;

        (await source.GetTokenAsync()).Should().Be(minted);
        (await source.GetTokenAsync()).Should().Be(minted);
        handler.Requests.Should().HaveCount(2, "the held token serves the third ask");
    }

    [Fact]
    public async Task AnUnreachableBroker_AnswersNull()
    {
        var (source, _) = Build(_ => throw new HttpRequestException("mycelium unreachable"));

        (await source.GetTokenAsync()).Should().BeNull();
    }
}
