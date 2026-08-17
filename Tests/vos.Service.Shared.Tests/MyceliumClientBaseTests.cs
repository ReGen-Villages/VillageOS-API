using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Shared.Tests;

public class MyceliumClientBaseTests
{
    private const string MyceliumUrl = "http://localhost:7243";
    private const string TestToken = "service-token-abc";

    // ---- HandlerId ----

    [Fact]
    public void HandlerId_IsUniquePerInstance()
    {
        var (a, _) = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.OK), serviceToken: TestToken);
        var (b, _) = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.OK), serviceToken: TestToken);

        a.HandlerId.Should().NotBe(Guid.Empty);
        a.HandlerId.Should().NotBe(b.HandlerId);
    }

    [Fact]
    public void MyceliumUrl_ExposesConstructorArgument()
    {
        var (client, _) = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.OK), serviceToken: TestToken);

        client.MyceliumUrl.Should().Be(MyceliumUrl);
    }

    // ---- GetTokenAsync ----

    [Fact]
    public async Task GetTokenAsync_WithProvidedToken_ReturnsItWithoutCallingMycelium()
    {
        var (client, handler) = BuildClient(_ =>
            throw new InvalidOperationException("Mycelium should not be contacted when --token is provided"),
            serviceToken: TestToken);

        var token = await client.GetTokenAsync();

        token.Should().Be(TestToken);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTokenAsync_NoToken_MyceliumReturnsToken_ReturnsMyceliumToken()
    {
        var (client, handler) = BuildClient(req =>
        {
            req.Method.Should().Be(HttpMethod.Post);
            req.RequestUri!.AbsoluteUri.Should().Be($"{MyceliumUrl}/api/auth/token");
            return JsonResponse("""{"token":"from-mycelium"}""");
        }, serviceToken: null);

        var token = await client.GetTokenAsync();

        token.Should().Be("from-mycelium");
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task GetTokenAsync_NoToken_MyceliumReturnsError_ReturnsNull()
    {
        var (client, _) = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            serviceToken: null);

        var token = await client.GetTokenAsync();

        token.Should().BeNull();
    }

    [Fact]
    public async Task GetTokenAsync_NoToken_HttpThrows_ReturnsNull()
    {
        var (client, _) = BuildClient(_ => throw new HttpRequestException("mycelium unreachable"),
            serviceToken: null);

        var token = await client.GetTokenAsync();

        token.Should().BeNull();
    }

    // Regression (#5894 / #5895): a daemon shared by several models must call back on the model
    // of the current /handle request. The inbound bearer overrides the launch-time startup token.
    [Fact]
    public async Task GetTokenAsync_PrefersInboundRequestToken_OverStartupToken()
    {
        var (client, _) = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.OK), serviceToken: TestToken);

        string? tokenInsideRequest = null;
        var pipeline = RequestTokenPipeline(async () => tokenInsideRequest = await client.GetTokenAsync());

        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Authorization = "Bearer inbound-model-token";
        await pipeline(ctx);

        tokenInsideRequest.Should().Be("inbound-model-token");
        (await client.GetTokenAsync()).Should().Be(TestToken,
            "outside a /handle request the startup token still applies");
    }

    // A subscription belongs to one model for as long as it is open, and its calls can be made from
    // inside a /handle request for a different one — the follower that opened it also adds subjects to
    // it from there. Its own token therefore has to win, or the subscription would be re-pointed at
    // whichever project the caller happened to be in.
    [Fact]
    public async Task GetTokenAsync_PrefersTheClientsOwnProvider_OverTheAmbientRequestToken()
    {
        var (client, _) = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.OK),
            serviceToken: TestToken, tokenProvider: () => Task.FromResult<string?>("this-clients-own-token"));

        string? tokenInsideRequest = null;
        var pipeline = RequestTokenPipeline(async () => tokenInsideRequest = await client.GetTokenAsync());

        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Authorization = "Bearer some-other-models-token";
        await pipeline(ctx);

        tokenInsideRequest.Should().Be("this-clients-own-token");
    }

    // The provider is asked on every call rather than read once, so a follower that replaces its bearer
    // does not have to rebuild the client — and a stale one is never sent after a replacement.
    [Fact]
    public async Task GetTokenAsync_AsksTheProviderEachTime_SoAReplacementTakesEffect()
    {
        var current = "first-token";
        var (client, _) = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.OK),
            serviceToken: null, tokenProvider: () => Task.FromResult<string?>(current));

        (await client.GetTokenAsync()).Should().Be("first-token");
        current = "replacement-token";

        (await client.GetTokenAsync()).Should().Be("replacement-token");
    }

    private static RequestDelegate RequestTokenPipeline(Func<Task> terminal)
    {
        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());
        app.UseMyceliumModelToken();
        app.Run(_ => terminal());
        return app.Build();
    }

    // ---- CreateAuthenticatedClientAsync ----

    [Fact]
    public async Task CreateAuthenticatedClient_WithToken_SetsBearerAuthorizationHeader()
    {
        var (client, _) = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.OK), serviceToken: TestToken);

        var httpClient = await client.CreateAuthenticatedClientPublicAsync();

        httpClient.DefaultRequestHeaders.Authorization.Should().NotBeNull();
        httpClient.DefaultRequestHeaders.Authorization!.Scheme.Should().Be("Bearer");
        httpClient.DefaultRequestHeaders.Authorization.Parameter.Should().Be(TestToken);
    }

    [Fact]
    public async Task CreateAuthenticatedClient_NoToken_MyceliumFails_ThrowsInvalidOperationException()
    {
        var (client, _) = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            serviceToken: null);

        var act = async () => await client.CreateAuthenticatedClientPublicAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CreateAuthenticatedClient_HonorsTimeoutOverride()
    {
        var (client, _) = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.OK), serviceToken: TestToken);

        var httpClient = await client.CreateAuthenticatedClientPublicAsync(TimeSpan.FromSeconds(42));

        httpClient.Timeout.Should().Be(TimeSpan.FromSeconds(42));
    }

    // ---- RegisterAsync ----

    [Fact]
    public async Task RegisterAsync_Success_PostsRegistrationEnvelopeAndReturnsTrue()
    {
        JsonElement? capturedBody = null;
        var (client, _) = BuildClient(req =>
        {
            req.Method.Should().Be(HttpMethod.Post);
            req.RequestUri!.AbsoluteUri.Should().Be($"{MyceliumUrl}/api/mycelium/register");
            capturedBody = ReadJsonBody(req);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, serviceToken: TestToken);

        var result = await client.RegisterAsync(port: 7100, serviceName: "Echo", startCommand: "endpoint-service");

        result.Should().BeTrue();
        capturedBody.Should().NotBeNull();
        var body = capturedBody!.Value;
        body.GetProperty("handlerId").GetString().Should().Be(client.HandlerId.ToString());
        body.GetProperty("serviceName").GetString().Should().Be("Echo");
        body.GetProperty("endpointUrl").GetString().Should().Be("http://localhost:7100");
        body.GetProperty("startCommand").GetString().Should().Be("endpoint-service");
        body.GetProperty("stopEndpoint").GetString().Should().Be("http://localhost:7100/shutdown");
        body.GetProperty("healthEndpoint").GetString().Should().Be("http://localhost:7100/health");
    }

    [Fact]
    public async Task RegisterAsync_MyceliumReturnsNonSuccess_ReturnsFalse()
    {
        var (client, _) = BuildClient(req =>
        {
            // Token leg returns success; registration leg returns failure.
            return req.RequestUri!.AbsolutePath == "/api/mycelium/register"
                ? new HttpResponseMessage(HttpStatusCode.BadRequest)
                : new HttpResponseMessage(HttpStatusCode.OK);
        }, serviceToken: TestToken);

        var result = await client.RegisterAsync(7100, "Echo", "endpoint-service");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task RegisterAsync_TokenAcquisitionFails_ReturnsFalse()
    {
        // No service token + mycelium returns 500 on /api/auth/token => CreateAuthenticatedClient throws
        // InvalidOperationException, which RegisterAsync catches.
        var (client, _) = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            serviceToken: null);

        var result = await client.RegisterAsync(7100, "Echo", "endpoint-service");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task RegisterAsync_HttpThrows_ReturnsFalse()
    {
        var (client, _) = BuildClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/mycelium/register")
                throw new HttpRequestException("network blew up");
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, serviceToken: TestToken);

        var result = await client.RegisterAsync(7100, "Echo", "endpoint-service");

        result.Should().BeFalse();
    }

    // ---- DeregisterAsync ----

    [Fact]
    public async Task DeregisterAsync_Success_SendsDeleteWithBearerHeader()
    {
        HttpRequestMessage? captured = null;
        var (client, _) = BuildClient(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, serviceToken: TestToken);

        await client.DeregisterAsync();

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Delete);
        captured.RequestUri!.AbsoluteUri.Should().Be($"{MyceliumUrl}/api/mycelium/services/{client.HandlerId}");
        captured.Headers.Authorization.Should().NotBeNull();
        captured.Headers.Authorization!.Scheme.Should().Be("Bearer");
        captured.Headers.Authorization.Parameter.Should().Be(TestToken);
    }

    [Fact]
    public async Task DeregisterAsync_NoToken_DoesNotSendRequest()
    {
        var (client, handler) = BuildClient(req =>
        {
            // /api/auth/token call only — registration leg should never be reached.
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }, serviceToken: null);

        await client.DeregisterAsync();

        handler.Requests.Should().OnlyContain(r => r.RequestUri!.AbsolutePath == "/api/auth/token");
    }

    [Fact]
    public async Task DeregisterAsync_NonSuccessStatus_DoesNotThrow()
    {
        var (client, _) = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound), serviceToken: TestToken);

        var act = async () => await client.DeregisterAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeregisterAsync_HttpThrows_DoesNotThrow()
    {
        var (client, _) = BuildClient(_ => throw new HttpRequestException("boom"), serviceToken: TestToken);

        var act = async () => await client.DeregisterAsync();

        await act.Should().NotThrowAsync();
    }

    // ---- Helpers ----

    private static (TestableMyceliumClient client, MockHttpMessageHandler handler) BuildClient(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        string? serviceToken,
        Func<Task<string?>>? tokenProvider = null)
    {
        var handler = new MockHttpMessageHandler(respond);
        var httpClient = new HttpClient(handler);
        var factory = new TestHttpClientFactory(httpClient);
        var client = new TestableMyceliumClient(factory, NullLogger.Instance, MyceliumUrl, serviceToken, tokenProvider);
        return (client, handler);
    }

    private static HttpResponseMessage JsonResponse(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private static JsonElement ReadJsonBody(HttpRequestMessage req)
    {
        var raw = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonSerializer.Deserialize<JsonElement>(raw);
    }
}
