using System.Net;
using System.Text.Json;
using vos.Service.Metabolism.Configuration;
using vos.Service.Metabolism.Services;
using vos.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace vos.Service.Metabolism.Tests;

public class MyceliumClientTests
{
    private readonly Mock<ILogger<MyceliumClient>> _logger = new();

    private MyceliumClient CreateUnreachableClient()
    {
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        return new MyceliumClient(httpFactory.Object, _logger.Object, "http://localhost:0", ResourceDirection.Consumes);
    }

    private MyceliumClient CreateMockedClient(MockHttpMessageHandler handler, ResourceDirection? direction = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://test-mycelium") };
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() =>
        {
            // Each call returns a fresh HttpClient sharing the same handler,
            // because MyceliumClient sets DefaultRequestHeaders per call.
            return new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("http://test-mycelium")
            };
        });
        return new MyceliumClient(httpFactory.Object, _logger.Object, "http://test-mycelium",
            direction ?? ResourceDirection.Consumes, serviceToken: "fake-jwt");
    }

    [Fact]
    public async Task ApplyQuantityAsync_Success_ReturnsJsonElement()
    {
        var mock = new MockHttpMessageHandler(req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"newValue\":42.5}", System.Text.Encoding.UTF8, "application/json")
            });

        var client = CreateMockedClient(mock, ResourceDirection.Consumes);

        var result = await client.ApplyQuantityAsync("thing-1", "quantity", 5.0m, "TestSubject", "kWh");

        result.Should().NotBeNull();
        result!.Value.GetProperty("newValue").GetDouble().Should().Be(42.5);
    }

    [Fact]
    public async Task ApplyQuantityAsync_NotFound_ThrowsKeyNotFoundException()
    {
        var mock = new MockHttpMessageHandler(req =>
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("Thing not found", System.Text.Encoding.UTF8, "text/plain")
            });

        var client = CreateMockedClient(mock, ResourceDirection.Consumes);

        var act = () => client.ApplyQuantityAsync("nonexistent", "quantity", 5.0m);
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task ApplyQuantityAsync_ServerError_ThrowsHttpRequestException()
    {
        var mock = new MockHttpMessageHandler(req =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("Internal error", System.Text.Encoding.UTF8, "text/plain")
            });

        var client = CreateMockedClient(mock, ResourceDirection.Consumes);

        var act = () => client.ApplyQuantityAsync("thing-1", "quantity", 5.0m);
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task ApplyQuantityAsync_ConsumingDirection_CallsDecrementEndpoint()
    {
        var mock = new MockHttpMessageHandler(req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}", System.Text.Encoding.UTF8, "application/json")
            });

        var client = CreateMockedClient(mock, ResourceDirection.Consumes);

        await client.ApplyQuantityAsync("thing-1", "quantity", 5.0m);

        var quantityRequest = mock.Requests.Should().ContainSingle().Subject;
        quantityRequest.RequestUri!.AbsolutePath.Should().Be("/api/things/thing-1/properties/quantity/decrements");
    }

    [Fact]
    public async Task ApplyQuantityAsync_ProducingDirection_CallsIncrementEndpoint()
    {
        var mock = new MockHttpMessageHandler(req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}", System.Text.Encoding.UTF8, "application/json")
            });

        var client = CreateMockedClient(mock, ResourceDirection.Produces);

        await client.ApplyQuantityAsync("thing-1", "quantity", 5.0m);

        var quantityRequest = mock.Requests.Should().ContainSingle().Subject;
        quantityRequest.RequestUri!.AbsolutePath.Should().Be("/api/things/thing-1/properties/quantity/increments");
    }

    [Fact]
    public async Task IncrementRelationshipPropertyAsync_Success_Completes()
    {
        var mock = new MockHttpMessageHandler(req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            });

        var client = CreateMockedClient(mock, ResourceDirection.Consumes);

        await client.IncrementRelationshipPropertyAsync("rel-1", "total_consumed", 10.0m);

        var relRequest = mock.Requests.First(r => r.RequestUri!.AbsolutePath.Contains("/api/relationships/"));
        relRequest.RequestUri!.AbsolutePath.Should().Be("/api/relationships/rel-1/properties/total_consumed/increments");
    }

    [Fact]
    public async Task IncrementRelationshipPropertyAsync_Failure_Throws()
    {
        var mock = new MockHttpMessageHandler(req =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("Server error", System.Text.Encoding.UTF8, "text/plain")
            });

        var client = CreateMockedClient(mock, ResourceDirection.Consumes);

        var act = () => client.IncrementRelationshipPropertyAsync("rel-1", "total_consumed", 10.0m);
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetTokenAsync_WithServiceToken_ReturnsTokenDirectly()
    {
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var client = new MyceliumClient(httpFactory.Object, _logger.Object, "http://localhost:0", ResourceDirection.Consumes, "my-service-token");

        var token = await client.GetTokenAsync();

        token.Should().Be("my-service-token");
    }

    [Fact]
    public async Task GetTokenAsync_WithoutAServiceToken_AnswersNothing()
    {
        var client = CreateUnreachableClient();

        var token = await client.GetTokenAsync();

        token.Should().BeNull("no key and no token was provided, so there is nothing to present");
    }

    [Fact]
    public async Task ApplyQuantityAsync_WithServiceToken_SkipsTokenEndpoint()
    {
        var mock = new MockHttpMessageHandler(req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}", System.Text.Encoding.UTF8, "application/json")
            });

        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() =>
            new HttpClient(mock, disposeHandler: false) { BaseAddress = new Uri("http://test-mycelium") });

        var client = new MyceliumClient(httpFactory.Object, _logger.Object, "http://test-mycelium", ResourceDirection.Consumes, "my-service-token");

        await client.ApplyQuantityAsync("thing-1", "quantity", 5.0m);

        mock.Requests.Should().NotContain(r => r.RequestUri!.AbsolutePath == "/api/auth/token");
        mock.Requests.Should().Contain(r => r.RequestUri!.AbsolutePath == "/api/things/thing-1/properties/quantity/decrements");
    }
}
