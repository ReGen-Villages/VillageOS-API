using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using vos.Service.Shared;
using Xunit;
using static vos.Service.Tributary.Tests.MyceliumStub;

namespace vos.Service.Tributary.Tests;

// Integration tests for the DiskCache kind through /handle (#5918): an endpoint reaching
// DiskCache through cachesBy serves a repeated fetch from local disk within cacheTtl —
// the upstream is contacted once. Combinations the cache cannot answer honestly (paging,
// a credentialed call, a request body that is not part of the key) are refused up front.
public class DiskCacheHandleTests : IDisposable
{
    private static readonly byte[] PngBytes = { 0x89, 0x50, 0x4E, 0x47, 0xFF, 0xFE, 0x00, 0x01 };

    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), "vos-cache-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_cacheDir)) Directory.Delete(_cacheDir, recursive: true);
    }

    private TributaryWebApplicationFactory Factory() => new() { CacheDirectory = _cacheDir };

    private static Kind DiskCache() => new(EndpointKindRoles.Caching, "DiskCache", "cacheTtl");
    private static Kind Binary() => new(EndpointKindRoles.ResponseBody, "BinaryResponse");

    [Fact]
    public async Task Handle_DiskCacheWithBinary_SecondCallServedFromDisk()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://tiles.test/tile/1/0/1"},
          "Endpoint.httpMethod": {"Value":"GET"},
          "Endpoint.cacheTtl":   {"Value":"300"}
        }
        """;
        var upstreamCalls = 0;
        await using var factory = Factory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "tiles.test") { upstreamCalls++; return MyceliumStub.Binary(PngBytes, "image/png"); }
            return RouteFindThing(req, thingId, "EP") ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKinds(req, thingId, DiskCache(), Binary())
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });
        var second = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        upstreamCalls.Should().Be(1, "the second call must be served from disk within the TTL");
        var firstBody = await first.Content.ReadAsStringAsync();
        var secondBody = await second.Content.ReadAsStringAsync();
        secondBody.Should().Be(firstBody);
        using var envelope = JsonDocument.Parse(secondBody);
        envelope.RootElement.GetProperty("contentType").GetString().Should().Be("image/png");
        Convert.FromBase64String(envelope.RootElement.GetProperty("dataBase64").GetString()!)
            .Should().Equal(PngBytes);
    }

    [Fact]
    public async Task Handle_DiskCachePlainBody_SecondCallServedFromDisk()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://api.test/data"},
          "Endpoint.httpMethod": {"Value":"GET"},
          "Endpoint.cacheTtl":   {"Value":"300"}
        }
        """;
        var upstreamCalls = 0;
        await using var factory = Factory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "api.test") { upstreamCalls++; return Json("""{"n":1}"""); }
            return RouteFindThing(req, thingId, "EP") ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKinds(req, thingId, DiskCache())
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });
        var second = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        upstreamCalls.Should().Be(1);
        (await second.Content.ReadAsStringAsync()).Should().Be("""{"n":1}""");
    }

    [Fact]
    public async Task Handle_DiskCache_MissingCacheTtl_RefusedByTheRequirementsGate()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://api.test/data"},
          "Endpoint.httpMethod": {"Value":"GET"}
        }
        """;
        await using var factory = Factory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
            RouteFindThing(req, thingId, "EP") ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKinds(req, thingId, DiskCache())
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("DiskCache").And.Contain("cacheTtl");
    }

    [Fact]
    public async Task Handle_DiskCache_NonPositiveCacheTtl_Refused()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://api.test/data"},
          "Endpoint.httpMethod": {"Value":"GET"},
          "Endpoint.cacheTtl":   {"Value":"nonsense"}
        }
        """;
        await using var factory = Factory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
            RouteFindThing(req, thingId, "EP") ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKinds(req, thingId, DiskCache())
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("cacheTtl");
    }

    [Fact]
    public async Task Handle_DiskCacheWithOffsetPaging_Refused()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":         {"Value":"https://api.test/data"},
          "Endpoint.httpMethod":  {"Value":"GET"},
          "Endpoint.cacheTtl":    {"Value":"300"},
          "Endpoint.offsetParam": {"Value":"offset"},
          "Endpoint.hasMorePath": {"Value":"more"},
          "Endpoint.itemsPath":   {"Value":"items"}
        }
        """;
        await using var factory = Factory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
            RouteFindThing(req, thingId, "EP") ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKinds(req, thingId, DiskCache(),
                    new Kind(EndpointKindRoles.Paging, "OffsetPaging", "offsetParam", "hasMorePath", "itemsPath"))
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("DiskCache").And.Contain("OffsetPaging");
    }

    [Fact]
    public async Task Handle_DiskCacheWithTokenExchange_Refused()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://api.test/data"},
          "Endpoint.httpMethod": {"Value":"GET"},
          "Endpoint.cacheTtl":   {"Value":"300"},
          "Endpoint.token":      {"Value":"PRE-MINTED"}
        }
        """;
        await using var factory = Factory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
            RouteFindThing(req, thingId, "EP") ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKinds(req, thingId, DiskCache(), new Kind(EndpointKindRoles.Authentication, "TokenExchangeAuth"))
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("DiskCache").And.Contain("TokenExchangeAuth");
    }

    [Fact]
    public async Task Handle_DiskCache_RequestWithOutboundBody_Refused()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://api.test/data"},
          "Endpoint.httpMethod": {"Value":"POST"},
          "Endpoint.cacheTtl":   {"Value":"300"}
        }
        """;
        await using var factory = Factory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
            RouteFindThing(req, thingId, "EP") ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKinds(req, thingId, DiskCache())
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP", body = new { q = 1 } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("body");
    }

    [Fact]
    public async Task Handle_UnknownCachingKind_RefusedNamingBothHalves()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://api.test/data"},
          "Endpoint.httpMethod": {"Value":"GET"}
        }
        """;
        await using var factory = Factory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
            RouteFindThing(req, thingId, "EP") ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKinds(req, thingId, new Kind(EndpointKindRoles.Caching, "RedisCache"))
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("RedisCache").And.Contain("DiskCache");
    }
}
