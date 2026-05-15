using System.Net;
using System.Text;
using vos.ManagedMicroservice.IntegrationRegistry.Models;
using vos.ManagedMicroservice.IntegrationRegistry.Services;
using vos.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace vos.ManagedMicroservice.IntegrationRegistry.Tests;

public class BrokerClientTests
{
    [Fact]
    public async Task CreateThingAsync_WhenBrokerReturnsThing_ReturnsParsedThing()
    {
        var thingId = Guid.NewGuid();
        var handler = new MockHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/things")
            {
                var json = $$"""
                {
                  "Id":"{{thingId}}",
                  "Name":"Endpoint",
                  "Properties":{"url":"https://api.example.com","httpMethod":"GET"}
                }
                """;
                return JsonResponse(json);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var sut = CreateClient(handler);
        var result = await sut.CreateThingAsync(new RegisterEndpointRequest
        {
            Name = "Endpoint",
            Properties = new Dictionary<string, object> { ["url"] = "https://api.example.com", ["httpMethod"] = "GET" }
        });

        result.Should().NotBeNull();
        result!.Value.Id.Should().Be(thingId);
        result.Value.Properties["httpMethod"].Should().Be("GET");
    }

    [Fact]
    public async Task SetThingPropertyAsync_WhenBrokerReturnsBadRequest_ReturnsFalse()
    {
        var thingId = Guid.NewGuid();
        var handler = new MockHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Put && request.RequestUri!.AbsolutePath == $"/api/things/{thingId}/properties")
                return new HttpResponseMessage(HttpStatusCode.BadRequest);

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var sut = CreateClient(handler);
        var result = await sut.SetThingPropertyAsync(thingId, "url", "not-a-url");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CreateRelationshipAsync_WhenBrokerReturnsCreated_ReturnsTrue()
    {
        var handler = new MockHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/relationships")
                return new HttpResponseMessage(HttpStatusCode.Created);

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var sut = CreateClient(handler);
        var result = await sut.CreateRelationshipAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        result.Should().BeTrue();
    }

    private static BrokerClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var factory = new TestHttpClientFactory(httpClient);
        var logger = Substitute.For<ILogger<BrokerClient>>();
        return new BrokerClient(factory, logger, "http://localhost", "test-token");
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
