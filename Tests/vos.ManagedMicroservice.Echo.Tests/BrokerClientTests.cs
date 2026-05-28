// Echo's BrokerClient is the canonical thin subclass of BrokerClientBase.
// These tests document the contract that EVERY microservice's BrokerClient
// subclass must satisfy:
//   1. Inherits HandlerId / BrokerUrl / GetTokenAsync / DeregisterAsync etc.
//   2. Provides at least one Echo-specific RegisterAsync overload that calls
//      base RegisterAsync with the service's own (serviceName, startCommand).
//   3. The body POSTed to /api/broker/register contains the service identity.
//
// New microservices' BrokerClient tests should mirror this file's shape:
// HandlerId-uniqueness, BrokerUrl-passthrough, RegisterAsync round-trip
// against MockHttpMessageHandler. See docs/MICROSERVICES.md §10.

using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.ManagedMicroservice.Echo.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.ManagedMicroservice.Echo.Tests;

public class BrokerClientTests
{
    private const string BrokerUrl = "http://localhost:7243";
    private const string ServiceToken = "svc-jwt-abc";

    [Fact]
    public void HandlerId_IsUniquePerInstance_PerTemplate()
    {
        var (a, _) = NewClient(_ => Ok());
        var (b, _) = NewClient(_ => Ok());

        a.HandlerId.Should().NotBe(Guid.Empty);
        a.HandlerId.Should().NotBe(b.HandlerId);
    }

    [Fact]
    public void BrokerUrl_PassedThroughFromCtor_PerTemplate()
    {
        var (client, _) = NewClient(_ => Ok());
        client.BrokerUrl.Should().Be(BrokerUrl);
    }

    [Fact]
    public async Task RegisterAsync_Success_PostsEchoServiceIdentity_PerTemplate()
    {
        JsonElement? capturedBody = null;
        var (client, _) = NewClient(req =>
        {
            req.Method.Should().Be(HttpMethod.Post);
            req.RequestUri!.AbsoluteUri.Should().Be($"{BrokerUrl}/api/broker/register");
            capturedBody = ReadJsonBody(req);
            return Ok();
        });

        var result = await client.RegisterAsync(port: 7100);

        result.Should().BeTrue();
        capturedBody.Should().NotBeNull();
        var body = capturedBody!.Value;

        // Echo-specific identity — these strings define what Echo declares to the broker.
        body.GetProperty("serviceName").GetString().Should().Be("Echo");
        body.GetProperty("startCommand").GetString().Should().Be("endpoint-service");

        // Template-required fields — every microservice's RegisterAsync sends these.
        body.GetProperty("handlerId").GetString().Should().Be(client.HandlerId.ToString());
        body.GetProperty("endpointUrl").GetString().Should().Be("http://localhost:7100");
        body.GetProperty("stopEndpoint").GetString().Should().Be("http://localhost:7100/shutdown");
        body.GetProperty("healthEndpoint").GetString().Should().Be("http://localhost:7100/health");
    }

    [Fact]
    public async Task RegisterAsync_BrokerReturnsFailure_ReturnsFalse_PerTemplate()
    {
        var (client, _) = NewClient(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));

        var result = await client.RegisterAsync(port: 7100);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task RegisterAsync_AuthFails_ReturnsFalse_PerTemplate()
    {
        // No service token + broker returns 500 on /api/auth/token →
        // CreateAuthenticatedClient throws InvalidOperationException →
        // RegisterAsync catches and returns false.
        var (client, _) = NewClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            serviceToken: null);

        var result = await client.RegisterAsync(port: 7100);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeregisterAsync_SendsDeleteToBroker_PerTemplate()
    {
        HttpRequestMessage? captured = null;
        var (client, _) = NewClient(req =>
        {
            captured = req;
            return Ok();
        });

        await client.DeregisterAsync();

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Delete);
        captured.RequestUri!.AbsoluteUri.Should().Be($"{BrokerUrl}/api/broker/services/{client.HandlerId}");
    }

    [Fact]
    public async Task GetTokenAsync_WithProvidedToken_ReturnsItDirectly_PerTemplate()
    {
        var (client, handler) = NewClient(_ =>
            throw new InvalidOperationException("Broker should not be contacted when --token is provided"));

        (await client.GetTokenAsync()).Should().Be(ServiceToken);
        handler.Requests.Should().BeEmpty();
    }

    // ---- Helpers (template-shape) ----

    private static (BrokerClient client, MockHttpMessageHandler handler) NewClient(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        string? serviceToken = ServiceToken)
    {
        var handler = new MockHttpMessageHandler(respond);
        var http = new HttpClient(handler);
        var factory = new TestHttpClientFactory(http);
        var client = new BrokerClient(factory, NullLogger<BrokerClient>.Instance, BrokerUrl, serviceToken);
        return (client, handler);
    }

    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK);

    private static JsonElement ReadJsonBody(HttpRequestMessage req)
    {
        var raw = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonSerializer.Deserialize<JsonElement>(raw);
    }
}
