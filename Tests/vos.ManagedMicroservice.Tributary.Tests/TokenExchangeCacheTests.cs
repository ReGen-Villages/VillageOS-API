using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using vos.ManagedMicroservice.Tributary.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.ManagedMicroservice.Tributary.Tests;

// Unit tests for the source-agnostic token-exchange cache (Task #5470). The cache POSTs the configured
// form fields to a token endpoint, reads the token/expiry by simple dotted path, caches per
// (url, fields), and refreshes at ~75% of lifetime. ESRI generateToken and OAuth2 client-credentials
// are exercised as two configurations of the same code. A FakeTimeProvider drives the clock.
public class TokenExchangeCacheTests
{
    private const string TokenUrl = "https://arcgis.test/tokens/generateToken";
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // ESRI-shaped request: form username/password/f, response { token, expires(epoch ms) }.
    private static TokenExchangeRequest EsriRequest(string url = TokenUrl, string user = "alice") => new(
        url,
        new Dictionary<string, string> { ["username"] = user, ["password"] = "s3cret", ["f"] = "json" },
        TokenPath: "token",
        ExpiryPath: "expires",
        ExpiryUnit: TokenExchangeCache.ExpiryUnitEpochMillis);

    [Fact]
    public async Task GetTokenAsync_FirstCall_PostsConfiguredFieldsAndReturnsTokenAtPath()
    {
        string? sentBody = null;
        var clock = new FakeTimeProvider(Start);
        var handler = new MockHttpMessageHandler(req =>
        {
            sentBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json($$"""{"token":"TKN1","expires":{{clock.GetUtcNow().AddHours(1).ToUnixTimeMilliseconds()}}}""");
        });

        var sut = Create(handler, clock);
        var token = await sut.GetTokenAsync(EsriRequest());

        token.Should().Be("TKN1");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.ToString().Should().Be(TokenUrl);
        sentBody.Should().NotBeNull();
        sentBody!.Should().Contain("username=alice").And.Contain("password=s3cret").And.Contain("f=json");
    }

    [Fact]
    public async Task GetTokenAsync_WithinFreshness_ReturnsCachedWithoutRefetch()
    {
        var clock = new FakeTimeProvider(Start);
        var handler = TokenHandler(clock);

        var sut = Create(handler, clock);
        var first = await sut.GetTokenAsync(EsriRequest());
        clock.Advance(TimeSpan.FromMinutes(30)); // below the 45-min (75%) threshold
        var second = await sut.GetTokenAsync(EsriRequest());

        second.Should().Be(first);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task GetTokenAsync_AfterRefreshThreshold_Refetches()
    {
        var clock = new FakeTimeProvider(Start);
        var handler = TokenHandler(clock);

        var sut = Create(handler, clock);
        var first = await sut.GetTokenAsync(EsriRequest());
        clock.Advance(TimeSpan.FromMinutes(50)); // past the 45-min threshold
        var second = await sut.GetTokenAsync(EsriRequest());

        second.Should().NotBe(first);
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetTokenAsync_DistinctRequestFields_CachedIndependently()
    {
        var clock = new FakeTimeProvider(Start);
        var handler = TokenHandler(clock);

        var sut = Create(handler, clock);
        await sut.GetTokenAsync(EsriRequest(user: "alice"));
        await sut.GetTokenAsync(EsriRequest(user: "bob"));

        handler.Requests.Should().HaveCount(2, "different credentials are a different cache key");
    }

    [Fact]
    public async Task GetTokenAsync_DistinctTokenUrl_CachedIndependently()
    {
        var clock = new FakeTimeProvider(Start);
        var handler = TokenHandler(clock);

        var sut = Create(handler, clock);
        await sut.GetTokenAsync(EsriRequest());
        await sut.GetTokenAsync(EsriRequest(url: "https://other.test/generateToken"));

        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetTokenAsync_NoTokenAtPath_Throws()
    {
        var clock = new FakeTimeProvider(Start);
        var handler = new MockHttpMessageHandler(_ => Json(
            """{"error":{"code":400,"message":"Invalid username or password."}}"""));

        var sut = Create(handler, clock);
        var act = async () => await sut.GetTokenAsync(EsriRequest());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetTokenAsync_NonSuccessStatus_Throws()
    {
        var clock = new FakeTimeProvider(Start);
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var sut = Create(handler, clock);
        var act = async () => await sut.GetTokenAsync(EsriRequest());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetTokenAsync_ConcurrentCallsSameKey_MintOnlyOneToken()
    {
        var clock = new FakeTimeProvider(Start);
        var handler = TokenHandler(clock);
        var sut = Create(handler, clock);

        var tokens = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => sut.GetTokenAsync(EsriRequest())));

        handler.Requests.Should().ContainSingle();
        tokens.Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task GetTokenAsync_OAuthShapeRelativeExpiry_ReadsAccessTokenAndExpiresIn()
    {
        // A different source entirely: OAuth2 client-credentials. Same code, different config.
        var clock = new FakeTimeProvider(Start);
        var calls = 0;
        var handler = new MockHttpMessageHandler(_ =>
        {
            calls++;
            return Json($$"""{"access_token":"AT{{calls}}","token_type":"Bearer","expires_in":3600}""");
        });
        var oauth = new TokenExchangeRequest(
            "https://idp.test/oauth/token",
            new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = "c", ["client_secret"] = "s" },
            TokenPath: "access_token",
            ExpiryPath: "expires_in",
            ExpiryUnit: TokenExchangeCache.ExpiryUnitSeconds);

        var sut = Create(handler, clock);
        var first = await sut.GetTokenAsync(oauth);
        clock.Advance(TimeSpan.FromMinutes(30));
        var cached = await sut.GetTokenAsync(oauth);
        clock.Advance(TimeSpan.FromMinutes(30)); // now 60 min in -> past 45-min threshold of a 3600s token
        var refreshed = await sut.GetTokenAsync(oauth);

        first.Should().Be("AT1");
        cached.Should().Be("AT1");
        refreshed.Should().Be("AT2");
    }

    [Fact]
    public async Task GetTokenAsync_NoExpiryConfigured_RefetchesEachCall()
    {
        var clock = new FakeTimeProvider(Start);
        var handler = TokenHandler(clock);
        var noExpiry = EsriRequest() with { ExpiryPath = null, ExpiryUnit = null };

        var sut = Create(handler, clock);
        await sut.GetTokenAsync(noExpiry);
        await sut.GetTokenAsync(noExpiry);

        handler.Requests.Should().HaveCount(2, "without a usable expiry the token is treated as immediately stale");
    }

    // ---------- helpers ----------

    private static TokenExchangeCache Create(HttpMessageHandler handler, TimeProvider clock) =>
        new(new PerCallFactory(handler), clock, Substitute.For<ILogger<TokenExchangeCache>>());

    private static MockHttpMessageHandler TokenHandler(TimeProvider clock)
    {
        var calls = 0;
        return new MockHttpMessageHandler(_ =>
        {
            calls++;
            return Json($$"""{"token":"TKN{{calls}}","expires":{{clock.GetUtcNow().AddHours(1).ToUnixTimeMilliseconds()}}}""");
        });
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class PerCallFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public PerCallFactory(HttpMessageHandler handler) { _handler = handler; }
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public FakeTimeProvider(DateTimeOffset start) => _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
