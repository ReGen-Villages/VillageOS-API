using System.Net;
using System.Text.Json;
using vos.ManagedMicroservice.Metabolism.Services;
using vos.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace vos.ManagedMicroservice.Metabolism.Tests;

public class BrokerClientTests
{
    private readonly Mock<ILogger<BrokerClient>> _logger = new();

    /// <summary>
    /// Create a BrokerClient whose GetTokenAsync() always fails (no reachable broker).
    /// </summary>
    private BrokerClient CreateUnreachableClient()
    {
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        return new BrokerClient(httpFactory.Object, _logger.Object, "http://localhost:0", "consumes");
    }

    /// <summary>
    /// Create a BrokerClient backed by a MockHttpMessageHandler so HTTP calls
    /// are intercepted without requiring a running broker.
    /// </summary>
    private BrokerClient CreateMockedClient(MockHttpMessageHandler handler, string mode = "consumes")
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://test-broker") };
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() =>
        {
            // Each call returns a fresh HttpClient sharing the same handler,
            // because BrokerClient sets DefaultRequestHeaders per call.
            return new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("http://test-broker")
            };
        });
        return new BrokerClient(httpFactory.Object, _logger.Object, "http://test-broker", mode);
    }

    /// <summary>
    /// Build a MockHttpMessageHandler that responds to /api/auth/token with a fake JWT
    /// and routes all other requests through the supplied responder.
    /// </summary>
    private static MockHttpMessageHandler CreateTokenAwareMock(
        Func<HttpRequestMessage, HttpResponseMessage> apiResponder)
    {
        return new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"token\":\"fake-jwt\"}", System.Text.Encoding.UTF8, "application/json")
                };
            }

            return apiResponder(req);
        });
    }

    #region ConnectSignalR Tests

    [Fact]
    public async Task ConnectSignalRAsync_RespectsImmediateCancellation()
    {
        var client = CreateUnreachableClient();
        var cts = new CancellationTokenSource();
        cts.Cancel(); // pre-cancelled

        // Should return immediately, not hang
        var task = client.ConnectSignalRAsync(cts.Token);
        var completed = await Task.WhenAny(task, Task.Delay(2000));
        completed.Should().Be(task, "ConnectSignalRAsync should respect cancellation immediately");
    }

    [Fact]
    public async Task ConnectSignalRAsync_RetriesWhenTokenUnavailable()
    {
        var client = CreateUnreachableClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // Broker unreachable → GetTokenAsync returns null → retries until cancelled
        await client.ConnectSignalRAsync(cts.Token);

        // Verify it logged retry warnings (at least one attempt)
        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("cannot get token")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce());
    }

    [Fact]
    public async Task ConnectSignalRAsync_StopsRetryingOnCancellation()
    {
        var client = CreateUnreachableClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await client.ConnectSignalRAsync(cts.Token);
        sw.Stop();

        // Should stop within a reasonable time after cancellation (not hang forever)
        sw.ElapsedMilliseconds.Should().BeLessThan(5000,
            "ConnectSignalRAsync should stop retrying when the token is cancelled");
    }

    [Fact]
    public void OnRelationshipPropertyChanged_IsSubscribableWithoutConnection()
    {
        var client = CreateUnreachableClient();

        Guid receivedId = Guid.Empty;

        client.OnRelationshipPropertyChanged += (id, prop, value) =>
        {
            receivedId = id;
        };

        // Event should be subscribable even without a SignalR connection.
        receivedId.Should().Be(Guid.Empty, "event should not fire until invoked");
    }

    #endregion

    #region ApplyQuantityAsync Tests

    [Fact]
    public async Task ApplyQuantityAsync_Success_ReturnsJsonElement()
    {
        var mock = CreateTokenAwareMock(req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"newValue\":42.5}", System.Text.Encoding.UTF8, "application/json")
            });

        var client = CreateMockedClient(mock, "consumes");

        var result = await client.ApplyQuantityAsync("thing-1", "quantity", 5.0m, "TestSubject", "kWh");

        result.Should().NotBeNull();
        result!.Value.GetProperty("newValue").GetDouble().Should().Be(42.5);
    }

    [Fact]
    public async Task ApplyQuantityAsync_NotFound_ThrowsKeyNotFoundException()
    {
        var mock = CreateTokenAwareMock(req =>
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("Thing not found", System.Text.Encoding.UTF8, "text/plain")
            });

        var client = CreateMockedClient(mock, "consumes");

        var act = () => client.ApplyQuantityAsync("nonexistent", "quantity", 5.0m);
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task ApplyQuantityAsync_ServerError_ThrowsHttpRequestException()
    {
        var mock = CreateTokenAwareMock(req =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("Internal error", System.Text.Encoding.UTF8, "text/plain")
            });

        var client = CreateMockedClient(mock, "consumes");

        var act = () => client.ApplyQuantityAsync("thing-1", "quantity", 5.0m);
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task ApplyQuantityAsync_ConsumesMode_CallsDecrementEndpoint()
    {
        var mock = CreateTokenAwareMock(req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}", System.Text.Encoding.UTF8, "application/json")
            });

        var client = CreateMockedClient(mock, "consumes");

        await client.ApplyQuantityAsync("thing-1", "quantity", 5.0m);

        // The second request (after token) should be the quantity call
        var quantityRequest = mock.Requests.First(r => r.RequestUri!.AbsolutePath != "/api/auth/token");
        quantityRequest.RequestUri!.AbsolutePath.Should().Be("/api/things/thing-1/properties/quantity/decrements");
    }

    [Fact]
    public async Task ApplyQuantityAsync_ProducesMode_CallsIncrementEndpoint()
    {
        var mock = CreateTokenAwareMock(req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}", System.Text.Encoding.UTF8, "application/json")
            });

        var client = CreateMockedClient(mock, "produces");

        await client.ApplyQuantityAsync("thing-1", "quantity", 5.0m);

        var quantityRequest = mock.Requests.First(r => r.RequestUri!.AbsolutePath != "/api/auth/token");
        quantityRequest.RequestUri!.AbsolutePath.Should().Be("/api/things/thing-1/properties/quantity/increments");
    }

    #endregion

    #region IncrementRelationshipPropertyAsync Tests

    [Fact]
    public async Task IncrementRelationshipPropertyAsync_Success_Completes()
    {
        var mock = CreateTokenAwareMock(req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            });

        var client = CreateMockedClient(mock, "consumes");

        // Should not throw
        await client.IncrementRelationshipPropertyAsync("rel-1", "total_consumed", 10.0m);

        var relRequest = mock.Requests.First(r => r.RequestUri!.AbsolutePath.Contains("/api/relationships/"));
        relRequest.RequestUri!.AbsolutePath.Should().Be("/api/relationships/rel-1/properties/total_consumed/increments");
    }

    [Fact]
    public async Task IncrementRelationshipPropertyAsync_Failure_Throws()
    {
        var mock = CreateTokenAwareMock(req =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("Server error", System.Text.Encoding.UTF8, "text/plain")
            });

        var client = CreateMockedClient(mock, "consumes");

        var act = () => client.IncrementRelationshipPropertyAsync("rel-1", "total_consumed", 10.0m);
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    #endregion

    #region Service Token Tests

    [Fact]
    public async Task GetTokenAsync_WithServiceToken_ReturnsTokenDirectly()
    {
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var client = new BrokerClient(httpFactory.Object, _logger.Object, "http://localhost:0", "consumes", "my-service-token");

        var token = await client.GetTokenAsync();

        token.Should().Be("my-service-token");
    }

    [Fact]
    public async Task GetTokenAsync_WithoutServiceToken_FallsBackToEndpoint()
    {
        // Without a service token, GetTokenAsync tries the broker endpoint (which will fail here)
        var client = CreateUnreachableClient();

        var token = await client.GetTokenAsync();

        token.Should().BeNull("broker is unreachable and no service token was provided");
    }

    [Fact]
    public async Task ApplyQuantityAsync_WithServiceToken_SkipsTokenEndpoint()
    {
        // All requests go through the same handler — no /api/auth/token call expected
        var mock = new MockHttpMessageHandler(req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}", System.Text.Encoding.UTF8, "application/json")
            });

        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() =>
            new HttpClient(mock, disposeHandler: false) { BaseAddress = new Uri("http://test-broker") });

        var client = new BrokerClient(httpFactory.Object, _logger.Object, "http://test-broker", "consumes", "my-service-token");

        await client.ApplyQuantityAsync("thing-1", "quantity", 5.0m);

        // Should NOT have called /api/auth/token
        mock.Requests.Should().NotContain(r => r.RequestUri!.AbsolutePath == "/api/auth/token");
        // Should have called the quantity endpoint
        mock.Requests.Should().Contain(r => r.RequestUri!.AbsolutePath == "/api/things/thing-1/properties/quantity/decrements");
    }

    #endregion
}
