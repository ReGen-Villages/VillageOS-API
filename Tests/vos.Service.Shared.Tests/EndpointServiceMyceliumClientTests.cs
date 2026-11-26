using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Shared.Tests;

// The broker client used by every service that only needs to register under its own name.
// Services with broker calls of their own derive from MyceliumClientBase and are covered
// alongside those calls.
public class EndpointServiceMyceliumClientTests
{
    private const string MyceliumUrl = "http://localhost:7243";
    private const string ServiceToken = "svc-jwt-abc";
    private const string ServiceName = "Echo";

    [Fact]
    public void HandlerId_DiffersBetweenInstances()
    {
        var (first, _) = NewClient(_ => Ok());
        var (second, _) = NewClient(_ => Ok());

        first.HandlerId.Should().NotBe(Guid.Empty);
        first.HandlerId.Should().NotBe(second.HandlerId);
    }

    [Fact]
    public void MyceliumUrl_IsTheOneItWasBuiltWith()
    {
        var (client, _) = NewClient(_ => Ok());

        client.MyceliumUrl.Should().Be(MyceliumUrl);
    }

    [Theory]
    [InlineData("Echo")]
    [InlineData("EnergyBalance")]
    [InlineData("WaterReserve")]
    [InlineData("ModelBridge")]
    public async Task RegisterUntilSettledAsync_DeclaresTheServiceUnderItsOwnName(string serviceName)
    {
        JsonElement? capturedBody = null;
        var (client, _) = NewClient(request =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.AbsoluteUri.Should().Be($"{MyceliumUrl}/api/mycelium/register");
            capturedBody = ReadJsonBody(request);
            return Ok();
        }, serviceName: serviceName);

        var result = await RegisterAsync(client);

        result.Should().BeTrue();
        capturedBody.Should().NotBeNull();
        capturedBody!.Value.GetProperty("serviceName").GetString().Should().Be(serviceName);
        capturedBody.Value.GetProperty("startCommand").GetString()
            .Should().Be(EndpointServiceMyceliumClient.StartCommand);
    }

    [Fact]
    public async Task RegisterUntilSettledAsync_SendsTheEndpointsMyceliumNeedsToReachTheService()
    {
        JsonElement? capturedBody = null;
        var (client, _) = NewClient(request =>
        {
            capturedBody = ReadJsonBody(request);
            return Ok();
        });

        await RegisterAsync(client);

        var body = capturedBody!.Value;
        body.GetProperty("handlerId").GetString().Should().Be(client.HandlerId.ToString());
        body.GetProperty("endpointUrl").GetString().Should().Be("http://localhost:7100");
        body.GetProperty("stopEndpoint").GetString().Should().Be("http://localhost:7100/shutdown");
        body.GetProperty("healthEndpoint").GetString().Should().Be("http://localhost:7100/health");
    }

    [Fact]
    public async Task RegisterUntilSettledAsync_PassesTheLogFilePrefixOn()
    {
        JsonElement? capturedBody = null;
        var (client, _) = NewClient(request =>
        {
            capturedBody = ReadJsonBody(request);
            return Ok();
        });

        await RegisterAsync(client, logFilePrefix: "echo-");

        capturedBody!.Value.GetProperty("logFilePrefix").GetString().Should().Be("echo-");
    }

    [Fact]
    public async Task RegisterUntilSettledAsync_WhenMyceliumRefuses_ReturnsFalse()
    {
        var (client, _) = NewClient(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));

        (await RegisterAsync(client)).Should().BeFalse();
    }

    [Fact]
    public async Task RegisterUntilSettledAsync_HoldingNoCredential_ReturnsFalseWithoutAsking()
    {
        var (client, handler) = NewClient(
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            serviceToken: null);

        (await RegisterAsync(client)).Should().BeFalse();
        handler.Requests.Should().BeEmpty();
    }

    private static Task<bool> RegisterAsync(EndpointServiceMyceliumClient client, string? logFilePrefix = null) =>
        client.RegisterUntilSettledAsync(7100, logFilePrefix, null, TimeProvider.System, CancellationToken.None);

    [Fact]
    public async Task GetTokenAsync_WithAnIssuedToken_DoesNotContactMycelium()
    {
        var (client, handler) = NewClient(_ =>
            throw new InvalidOperationException("Mycelium should not be contacted when a token is supplied"));

        (await client.GetTokenAsync()).Should().Be(ServiceToken);
        handler.Requests.Should().BeEmpty();
    }

    private static (EndpointServiceMyceliumClient Client, MockHttpMessageHandler Handler) NewClient(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        string? serviceToken = ServiceToken,
        string serviceName = ServiceName)
    {
        var handler = new MockHttpMessageHandler(respond);
        var factory = new TestHttpClientFactory(new HttpClient(handler));
        var client = new EndpointServiceMyceliumClient(
            factory,
            NullLogger<EndpointServiceMyceliumClient>.Instance,
            serviceName,
            MyceliumUrl,
            serviceToken is null ? null : new LaunchToken(serviceToken));
        return (client, handler);
    }

    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK);

    private static JsonElement ReadJsonBody(HttpRequestMessage request)
    {
        var raw = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonSerializer.Deserialize<JsonElement>(raw);
    }
}
