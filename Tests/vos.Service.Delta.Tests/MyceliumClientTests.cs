using System.Net;
using System.Text;
using System.Text.Json;
using vos.Service.Delta.Models;
using vos.Service.Delta.Services;
using vos.Service.Shared;
using vos.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace vos.Service.Delta.Tests;

public class MyceliumClientTests
{

    [Fact]
    public async Task GetResolvedProperties_KeepsEachKeyAsTheBrokerNamedIt()
    {
        var thingId = Guid.NewGuid();
        var handler = new MockHttpMessageHandler(request =>
            request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == $"/api/things/{thingId}/properties"
                ? JsonResponse($$"""
                  {
                    "layer": { "Value": "4", "Type": "vos.String", "IsInherited": false, "InheritedFrom": null },
                    "EsriEndpoint.Endpoint.url": { "Value": "", "Type": "vos.String", "IsInherited": true, "InheritedFrom": "{{Guid.NewGuid()}}" }
                  }
                  """)
                : new HttpResponseMessage(HttpStatusCode.NotFound));

        var resolved = await CreateClient(handler).GetResolvedPropertiesAsync(thingId);

        resolved.Should().NotBeNull();
        resolved!.Keys.Should().BeEquivalentTo("layer", "EsriEndpoint.Endpoint.url");
        resolved["LAYER"].GetString().Should().Be("4", "a key is matched whatever its case");
    }

    [Fact]
    public async Task GetResolvedProperties_NonSuccessStatus_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        (await CreateClient(handler).GetResolvedPropertiesAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task GetResolvedProperties_NonObjectBody_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler(_ => JsonResponse("[1,2,3]"));
        (await CreateClient(handler).GetResolvedPropertiesAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task GetResolvedProperties_TransportThrows_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler(_ => throw new HttpRequestException("boom"));
        (await CreateClient(handler).GetResolvedPropertiesAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task CreateThing_ObjectMissingId_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler(_ => JsonResponse("""{"Name":"NoIdHere"}"""));
        (await CreateClient(handler).CreateThingAsync(new() { Name = "X" })).Should().BeNull();
    }

    [Fact]
    public async Task CreateThing_ObjectIdNotAGuid_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler(_ => JsonResponse("""{"Id":"not-a-guid","Name":"X"}"""));
        (await CreateClient(handler).CreateThingAsync(new() { Name = "X" })).Should().BeNull();
    }

    [Fact]
    public async Task CreateThing_ObjectMissingName_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler(_ =>
            JsonResponse($$"""{"Id":"{{Guid.NewGuid()}}"}"""));
        (await CreateClient(handler).CreateThingAsync(new() { Name = "X" })).Should().BeNull();
    }

    [Fact]
    public async Task CreateThing_CaseInsensitiveIdNameKeys_ParsesThing()
    {
        var id = Guid.NewGuid();
        var handler = new MockHttpMessageHandler(_ =>
            JsonResponse($$"""{"id":"{{id}}","name":"lower"}"""));
        var result = await CreateClient(handler).CreateThingAsync(new() { Name = "lower" });
        result.Should().NotBeNull();
        result!.Value.Name.Should().Be("lower");
    }

    [Fact]
    public async Task CreateThing_PropertiesObjectPresent_PopulatesEveryValueKind()
    {
        // Exercises every JsonValueKind switch arm in TryParseThing's properties loop:
        // String, Number(Int64), Number(Double), True, False, Null, default.
        var id = Guid.NewGuid();
        var handler = new MockHttpMessageHandler(_ => JsonResponse($$"""
        {
          "Id":"{{id}}",
          "Name":"WithProps",
          "Properties":{
            "s": "hello",
            "i": 42,
            "d": 3.14,
            "t": true,
            "f": false,
            "n": null,
            "arr": [1,2]
          }
        }
        """));

        var result = await CreateClient(handler).CreateThingAsync(new() { Name = "WithProps" });

        result.Should().NotBeNull();
        result!.Value.Properties["s"].Should().Be("hello");
        result.Value.Properties["i"].Should().Be(42L);
        result.Value.Properties["d"].Should().Be(3.14);
        result.Value.Properties["t"].Should().Be(true);
        result.Value.Properties["f"].Should().Be(false);
        result.Value.Properties["n"].Should().BeNull();
        // Array arm falls through to default → GetRawText
        result.Value.Properties["arr"].Should().Be("[1,2]");
    }

    [Fact]
    public async Task CreateThingAsync_WhenMyceliumReturnsThing_ReturnsParsedThing()
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
    public async Task CreateThingAsync_NonSuccessStatus_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("bad", Encoding.UTF8, "text/plain")
        });
        (await CreateClient(handler).CreateThingAsync(new() { Name = "X" })).Should().BeNull();
    }

    [Fact]
    public async Task CreateThingAsync_TransportThrows_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler(_ => throw new HttpRequestException("boom"));
        (await CreateClient(handler).CreateThingAsync(new() { Name = "X" })).Should().BeNull();
    }

    [Fact]
    public async Task CreateThingAsync_ResponseUnparseable_ReturnsNull()
    {
        // 200 OK but the body isn't a JSON object → TryParseThing returns false.
        var handler = new MockHttpMessageHandler(_ => JsonResponse("[1,2,3]"));
        (await CreateClient(handler).CreateThingAsync(new() { Name = "X" })).Should().BeNull();
    }

    [Fact]
    public async Task DeleteThingAsync_HappyPath_ReturnsTrue()
    {
        var id = Guid.NewGuid();
        var handler = new MockHttpMessageHandler(req =>
            req.Method == HttpMethod.Delete && req.RequestUri!.AbsolutePath == $"/api/things/{id}"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : new HttpResponseMessage(HttpStatusCode.NotFound));

        (await CreateClient(handler).DeleteThingAsync(id)).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteThingAsync_NonSuccessStatus_ReturnsFalse()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("missing", Encoding.UTF8, "text/plain")
        });
        (await CreateClient(handler).DeleteThingAsync(Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteThingAsync_TransportThrows_ReturnsFalse()
    {
        var handler = new MockHttpMessageHandler(_ => throw new HttpRequestException("boom"));
        (await CreateClient(handler).DeleteThingAsync(Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public async Task SetThingPropertyAsync_HappyPath_ReturnsTrue()
    {
        var id = Guid.NewGuid();
        var handler = new MockHttpMessageHandler(req =>
            req.Method == HttpMethod.Put && req.RequestUri!.AbsolutePath == $"/api/things/{id}/properties"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : new HttpResponseMessage(HttpStatusCode.NotFound));

        (await CreateClient(handler).SetThingPropertyAsync(id, "url", "https://x")).Should().BeTrue();
    }

    [Fact]
    public async Task SetThingPropertyAsync_WhenMyceliumReturnsBadRequest_ReturnsFalse()
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
    public async Task SetThingPropertyAsync_TransportThrows_ReturnsFalse()
    {
        var handler = new MockHttpMessageHandler(_ => throw new HttpRequestException("boom"));
        (await CreateClient(handler).SetThingPropertyAsync(Guid.NewGuid(), "p", "v")).Should().BeFalse();
    }

    // Bodies are captured camelCase, per PutAsJsonAsync's Web policy.

    [Theory]
    [InlineData("a string value", "vos.String", "\"a string value\"")]
    [InlineData(true, "vos.Boolean", "true")]
    [InlineData(false, "vos.Boolean", "false")]
    [InlineData(42, "vos.Double", "42")]
    [InlineData(42L, "vos.Double", "42")]
    [InlineData(3.5, "vos.Double", "3.5")]
    public async Task SetThingPropertyAsync_PlainPrimitive_SendsThePlatformsTypeName(
        object value, string expectedType, string expectedValueJson)
    {
        var body = await CaptureSetPropertyBody(value);
        body.Should().Contain($"\"type\":\"{expectedType}\"");
        body.Should().Contain($"\"value\":{expectedValueJson}");
    }

    [Fact]
    public async Task SetThingPropertyAsync_NullValue_SendsAString()
    {
        var body = await CaptureSetPropertyBody(null);
        body.Should().Contain("\"type\":\"vos.String\"");
        body.Should().Contain("\"value\":null");
    }

    [Theory]
    [InlineData("\"hello\"", "vos.String", "\"hello\"")]
    [InlineData("42", "vos.Double", "42")]
    [InlineData("3.5", "vos.Double", "3.5")]
    [InlineData("true", "vos.Boolean", "true")]
    [InlineData("false", "vos.Boolean", "false")]
    [InlineData("null", "vos.String", "null")]
    [InlineData("[1,2,3]", "vos.String", "\"[1,2,3]\"")]
    public async Task SetThingPropertyAsync_JsonElementValue_SendsThePlatformsTypeName(
        string elementJson, string expectedType, string expectedValueJson)
    {
        var element = JsonDocument.Parse(elementJson).RootElement;
        var body = await CaptureSetPropertyBody(element);
        body.Should().Contain($"\"type\":\"{expectedType}\"");
        body.Should().Contain($"\"value\":{expectedValueJson}");
    }

    [Fact]
    public async Task SetThingPropertyAsync_AWholeNumberTooLargeForADouble_IsSentExactly()
    {
        var body = await CaptureSetPropertyBody(JsonDocument.Parse("9007199254740993").RootElement);

        body.Should().Contain("\"value\":9007199254740993");
    }

    [Theory]
    [InlineData("\"hello\"")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("{\"x\":1}")]
    public async Task SetThingPropertyAsync_SendsOnlyTypeNamesTheSharedListHolds(string elementJson)
    {
        var body = JsonDocument.Parse(await CaptureSetPropertyBody(JsonDocument.Parse(elementJson).RootElement)).RootElement;

        TypedProperties.TypeNames.Should().Contain(body.GetProperty("type").GetString());
    }

    [Fact]
    public async Task CreateRelationshipAsync_WhenMyceliumReturnsCreated_ReturnsTrue()
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

    [Fact]
    public async Task CreateRelationshipAsync_NonSuccessStatus_ReturnsFalse()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent("dup", Encoding.UTF8, "text/plain")
        });

        (await CreateClient(handler).CreateRelationshipAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()))
            .Should().BeFalse();
    }

    [Fact]
    public async Task CreateRelationshipAsync_TransportThrows_ReturnsFalse()
    {
        var handler = new MockHttpMessageHandler(_ => throw new HttpRequestException("boom"));

        (await CreateClient(handler).CreateRelationshipAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()))
            .Should().BeFalse();
    }

    private static MyceliumClient CreateClient(HttpMessageHandler handler)
    {
        // PerCallFactory because MyceliumClientBase.CreateAuthenticatedClientAsync mutates
        // client.Timeout on every call; reusing one HttpClient throws after the first request.
        var factory = new PerCallFactory(handler);
        var logger = Substitute.For<ILogger<MyceliumClient>>();
        return new MyceliumClient(factory, logger, "http://localhost", "test-token");
    }

    private sealed class PerCallFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public PerCallFactory(HttpMessageHandler handler) { _handler = handler; }
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static async Task<string> CaptureSetPropertyBody(object? value)
    {
        string captured = string.Empty;
        var thingId = Guid.NewGuid();
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Put && req.RequestUri!.AbsolutePath == $"/api/things/{thingId}/properties")
            {
                captured = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        await CreateClient(handler).SetThingPropertyAsync(thingId, "p", value);
        return captured;
    }
}
