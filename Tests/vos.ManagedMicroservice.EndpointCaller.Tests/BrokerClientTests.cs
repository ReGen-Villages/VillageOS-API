using System.Net;
using System.Text;
using vos.ManagedMicroservice.EndpointCaller.Services;
using vos.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace vos.ManagedMicroservice.EndpointCaller.Tests;

public class BrokerClientTests
{
    [Fact]
    public async Task FindThingByNameAsync_WhenBrokerReturnsThing_ReturnsParsedThing()
    {
        var thingId = Guid.NewGuid();
        var handler = new MockHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/things")
            {
                var json = $$"""{"Id":"{{thingId}}","Name":"OpenMeteoEndpoint"}""";
                return JsonResponse(json);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var sut = CreateClient(handler);
        var result = await sut.FindThingByNameAsync("OpenMeteoEndpoint");

        result.Should().NotBeNull();
        result!.Value.Id.Should().Be(thingId);
        result.Value.Name.Should().Be("OpenMeteoEndpoint");
    }

    [Fact]
    public async Task GetEffectivePropertiesAsync_WhenBrokerReturnsValueEnvelope_ExtractsValues()
    {
        var thingId = Guid.NewGuid();
        var handler = new MockHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == $"/api/things/{thingId}/effective-properties")
            {
                var json = """
                {
                  "Endpoint.url": {"Value":"https://example.com/api"},
                  "Endpoint.httpMethod": {"Value":"GET"}
                }
                """;
                return JsonResponse(json);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var sut = CreateClient(handler);
        var result = await sut.GetEffectivePropertiesAsync(thingId);

        result.Should().NotBeNull();
        result!.Should().ContainKey("Endpoint.url");
        result["Endpoint.url"].GetString().Should().Be("https://example.com/api");
        result["Endpoint.httpMethod"].GetString().Should().Be("GET");
    }

    [Fact]
    public async Task SetThingPropertyAsync_WhenBrokerReturnsSuccess_ReturnsTrue()
    {
        var thingId = Guid.NewGuid();
        var handler = new MockHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Put && request.RequestUri!.AbsolutePath == $"/api/things/{thingId}/properties")
                return new HttpResponseMessage(HttpStatusCode.OK);

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var sut = CreateClient(handler);
        var result = await sut.SetThingPropertyAsync(thingId, "responseTransform", "$$.foo");

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
