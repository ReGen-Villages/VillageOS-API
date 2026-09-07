using System.Net;
using System.Text;
using System.Text.Json;
using vos.Service.Shared;
using vos.Service.Tributary.Services;
using vos.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace vos.Service.Tributary.Tests;

public class MyceliumClientTests
{
    // ---------- FindThingByNameAsync ----------

    [Fact]
    public async Task FindThingByNameAsync_WhenMyceliumReturnsThing_ReturnsParsedThing()
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
    public async Task FindThingByNameAsync_NonSuccessStatus_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = CreateClient(handler);

        (await sut.FindThingByNameAsync("Missing")).Should().BeNull();
    }

    [Fact]
    public async Task FindThingByNameAsync_NonObjectBody_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler(_ => JsonResponse("[1,2,3]"));
        var sut = CreateClient(handler);

        (await sut.FindThingByNameAsync("Anything")).Should().BeNull();
    }

    [Fact]
    public async Task FindThingByNameAsync_ObjectMissingId_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler(_ => JsonResponse("""{"Name":"NoIdHere"}"""));
        var sut = CreateClient(handler);

        (await sut.FindThingByNameAsync("Anything")).Should().BeNull();
    }

    [Fact]
    public async Task FindThingByNameAsync_ObjectIdNotAGuid_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler(_ => JsonResponse("""{"Id":"not-a-guid","Name":"X"}"""));
        var sut = CreateClient(handler);

        (await sut.FindThingByNameAsync("Anything")).Should().BeNull();
    }

    [Fact]
    public async Task FindThingByNameAsync_ObjectMissingName_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler(_ =>
            JsonResponse($$"""{"Id":"{{Guid.NewGuid()}}"}"""));
        var sut = CreateClient(handler);

        (await sut.FindThingByNameAsync("Anything")).Should().BeNull();
    }

    [Fact]
    public async Task FindThingByNameAsync_TransportThrows_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler(_ => throw new HttpRequestException("boom"));
        var sut = CreateClient(handler);

        (await sut.FindThingByNameAsync("X")).Should().BeNull();
    }

    [Fact]
    public async Task FindThingByNameAsync_CaseInsensitiveIdNameKeys_ParsesThing()
    {
        var thingId = Guid.NewGuid();
        var handler = new MockHttpMessageHandler(_ =>
            JsonResponse($$"""{"id":"{{thingId}}","name":"lowercase"}"""));
        var sut = CreateClient(handler);

        var result = await sut.FindThingByNameAsync("lowercase");

        result.Should().NotBeNull();
        result!.Value.Name.Should().Be("lowercase");
    }

    // ---------- GetEffectivePropertiesAsync ----------

    [Fact]
    public async Task GetEffectivePropertiesAsync_WhenMyceliumReturnsValueEnvelope_ExtractsValues()
    {
        var thingId = Guid.NewGuid();
        var handler = new MockHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == $"/api/things/{thingId}/properties")
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
        result!["Endpoint.url"].GetString().Should().Be("https://example.com/api");
        result["Endpoint.httpMethod"].GetString().Should().Be("GET");
    }

    [Fact]
    public async Task GetEffectivePropertiesAsync_NonSuccessStatus_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = CreateClient(handler);

        (await sut.GetEffectivePropertiesAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task GetEffectivePropertiesAsync_NonObjectBody_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler(_ => JsonResponse("[1,2,3]"));
        var sut = CreateClient(handler);

        (await sut.GetEffectivePropertiesAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task GetEffectivePropertiesAsync_TransportThrows_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler(_ => throw new HttpRequestException("boom"));
        var sut = CreateClient(handler);

        (await sut.GetEffectivePropertiesAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task GetEffectivePropertiesAsync_PropertyMissingValueKey_IsSkipped()
    {
        var handler = new MockHttpMessageHandler(_ => JsonResponse("""
        {
          "good": {"Value":"v"},
          "bad":  {"NoValue":"x"}
        }
        """));
        var sut = CreateClient(handler);

        var result = await sut.GetEffectivePropertiesAsync(Guid.NewGuid());

        result.Should().NotBeNull();
        result!.Should().ContainKey("good");
        result.Should().NotContainKey("bad");
    }

    // ---------- CreateThingAsync ----------

    [Fact]
    public async Task CreateThingAsync_HappyPath_ReturnsCreated()
    {
        var newId = Guid.NewGuid();
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/things")
                return JsonResponse($$"""{"Id":"{{newId}}","Name":"X"}""");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var sut = CreateClient(handler);
        var result = await sut.CreateThingAsync("X");

        result.Should().NotBeNull();
        result!.Value.Id.Should().Be(newId);
    }

    // The broker takes a property with its type on it and refuses a bare value, so what this asserts
    // is the envelope rather than the reading as it arrived (#6930). Asserting the bare shape is what
    // agreed with the writer about a rule neither of them applied; the case that could not is in the
    // broker-contract suite, which writes into the real engine.
    [Fact]
    public async Task CreateThingAsync_WithProperties_PostsEachOneWithItsType()
    {
        var newId = Guid.NewGuid();
        string? sentBody = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/things")
            {
                sentBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return JsonResponse($$"""{"Id":"{{newId}}","Name":"X"}""");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var sut = CreateClient(handler);
        await sut.CreateThingAsync("X", new Dictionary<string, object?> { ["k"] = 1 });

        sentBody.Should().NotBeNull();
        sentBody!.Should().Contain("\"k\":{\"typeInfo\":\"vos.Double\",\"value\":1}");
    }

    [Fact]
    public async Task CreateThingAsync_NonSuccessStatus_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("bad", Encoding.UTF8, "text/plain")
        });
        var sut = CreateClient(handler);

        (await sut.CreateThingAsync("X")).Should().BeNull();
    }

    [Fact]
    public async Task CreateThingAsync_TransportThrows_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler(_ => throw new HttpRequestException("boom"));
        var sut = CreateClient(handler);

        (await sut.CreateThingAsync("X")).Should().BeNull();
    }

    // ---------- CreateRelationshipAsync ----------

    [Fact]
    public async Task CreateRelationshipAsync_HappyPath_ReturnsTrue()
    {
        var handler = new MockHttpMessageHandler(req =>
            req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/relationships"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : new HttpResponseMessage(HttpStatusCode.NotFound));

        var sut = CreateClient(handler);

        (await sut.CreateRelationshipAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())).Should().BeTrue();
    }

    [Fact]
    public async Task CreateRelationshipAsync_NonSuccessStatus_ReturnsFalse()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent("dup", Encoding.UTF8, "text/plain")
        });
        var sut = CreateClient(handler);

        (await sut.CreateRelationshipAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public async Task CreateRelationshipAsync_TransportThrows_ReturnsFalse()
    {
        var handler = new MockHttpMessageHandler(_ => throw new HttpRequestException("boom"));
        var sut = CreateClient(handler);

        (await sut.CreateRelationshipAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())).Should().BeFalse();
    }

    // ---------- Helpers ----------

    // ---------- SubmitObservationsAsync ----------

    [Fact]
    public async Task SubmitObservationsAsync_PostsBatchToObservationsRoute()
    {
        var thingId = Guid.NewGuid();
        string path = string.Empty, body = string.Empty;
        var handler = new MockHttpMessageHandler(req =>
        {
            path = req.RequestUri!.AbsolutePath;
            body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            return JsonResponse("""{"accepted":2}""");
        });
        var sut = CreateClient(handler);

        var at = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc);
        var ok = await sut.SubmitObservationsAsync(thingId, new[]
        {
            new ObservationSample("temp", 21.0, at),
            new ObservationSample("humidity", 55.0, at),
        });

        ok.Should().BeTrue();
        path.Should().Be($"/api/things/{thingId}/observations");
        body.Should().Contain("\"property\":\"temp\"").And.Contain("\"value\":21").And.Contain("observedAt");
    }

    [Fact]
    public async Task SubmitObservationsAsync_OmitsObservedAtWhenTheSampleHasNone()
    {
        var body = string.Empty;
        var handler = new MockHttpMessageHandler(req =>
        {
            body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            return JsonResponse("""{"accepted":1}""");
        });
        var sut = CreateClient(handler);

        var ok = await sut.SubmitObservationsAsync(Guid.NewGuid(), new[]
        {
            new ObservationSample("temp", 21.0),
        });

        ok.Should().BeTrue();
        body.Should().NotContain("observedAt",
            "sending it as null would be the wall clock by another name; leaving it out lets "
            + "Mycelium stamp the whole batch once from the model clock");
    }

    // A reshape's output arrives as JsonElement, so each value kind has to reach Mycelium as the
    // scalar it stands for rather than its JSON text.
    [Theory]
    [InlineData("\"hello\"", "\"hello\"")]
    [InlineData("42", "42")]
    [InlineData("3.5", "3.5")]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    [InlineData("null", "null")]
    [InlineData("[1,2,3]", "\"[1,2,3]\"")]
    public async Task SubmitObservationsAsync_JsonElementValue_SerializesByValueKind(
        string elementJson, string expectedValueJson)
    {
        var element = JsonDocument.Parse(elementJson).RootElement;

        var body = await CaptureObservationBody(element);

        body.Should().Contain($"\"value\":{expectedValueJson}");
    }

    [Fact]
    public async Task SubmitObservationsAsync_EmptyBatch_ShortCircuitsToTrue()
    {
        var called = false;
        var handler = new MockHttpMessageHandler(_ => { called = true; return JsonResponse("{}"); });
        var sut = CreateClient(handler);

        var ok = await sut.SubmitObservationsAsync(Guid.NewGuid(), Array.Empty<ObservationSample>());

        ok.Should().BeTrue();
        called.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitObservationsAsync_NonSuccess_ReturnsFalse()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var sut = CreateClient(handler);

        var ok = await sut.SubmitObservationsAsync(Guid.NewGuid(),
            new[] { new ObservationSample("temp", 1.0, DateTime.UtcNow) });

        ok.Should().BeFalse();
    }

    // ---------- SetPropertyModeAsync ----------

    [Fact]
    public async Task SetPropertyModeAsync_PutsModeToPropertyModeRoute()
    {
        var thingId = Guid.NewGuid();
        string path = string.Empty, body = string.Empty;
        var handler = new MockHttpMessageHandler(req =>
        {
            path = req.RequestUri!.AbsolutePath;
            body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var sut = CreateClient(handler);

        var ok = await sut.SetPropertyModeAsync(thingId, "temp", "Sampled");

        ok.Should().BeTrue();
        path.Should().Be($"/api/things/{thingId}/properties/temp/mode");
        body.Should().Contain("Sampled");
    }

    [Fact]
    public async Task SetPropertyModeAsync_NonSuccess_ReturnsFalse()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var sut = CreateClient(handler);

        var ok = await sut.SetPropertyModeAsync(Guid.NewGuid(), "temp", "Sampled");

        ok.Should().BeFalse();
    }

    private static MyceliumClient CreateClient(HttpMessageHandler handler)
    {
        // Fresh HttpClient per CreateClient call so MyceliumClientBase.CreateAuthenticatedClientAsync
        // can set client.Timeout without tripping the "already started" guard on a reused client.
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

    private static async Task<string> CaptureObservationBody(object? value)
    {
        string capturedBody = string.Empty;
        var thingId = Guid.NewGuid();
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == $"/api/things/{thingId}/observations")
            {
                capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
                return JsonResponse("""{"accepted":1}""");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var sut = CreateClient(handler);
        await sut.SubmitObservationsAsync(thingId, new[] { new ObservationSample("p", value) });
        return capturedBody;
    }
}
