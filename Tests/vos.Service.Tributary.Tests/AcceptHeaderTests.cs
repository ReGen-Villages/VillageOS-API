using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using static vos.Service.Tributary.Tests.MyceliumStub;

namespace vos.Service.Tributary.Tests;

// Integration tests for the acceptHeader structural key through /handle: when the endpoint
// template carries acceptHeader, the outbound request must send exactly that Accept value —
// needed for upstreams that content-negotiate (e.g. image/tiff from an ArcGIS ImageServer).
// Absent the key, no Accept header is invented.
public class AcceptHeaderTests
{
    private static readonly byte[] TiffBytes = { 0x49, 0x49, 0x2A, 0x00, 0xFF, 0xFE, 0x00, 0x01 };

    [Fact]
    public async Task Handle_AcceptHeaderConfigured_SetsAcceptOnOutboundRequest()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":          {"Value":"https://api.test/data"},
          "Endpoint.httpMethod":   {"Value":"GET"},
          "Endpoint.acceptHeader": {"Value":"application/geo+json"}
        }
        """;
        HttpRequestMessage? outbound = null;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "api.test") { outbound = req; return Json("""{"ok":true}"""); }
            return RouteFindThing(req, thingId, "EP") ?? RouteEffectiveProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        outbound!.Headers.TryGetValues("Accept", out var values).Should().BeTrue();
        values!.Should().ContainSingle().Which.Should().Be("application/geo+json");
    }

    [Fact]
    public async Task Handle_NoAcceptHeader_OutboundHasNoAccept()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://api.test/data"},
          "Endpoint.httpMethod": {"Value":"GET"}
        }
        """;
        HttpRequestMessage? outbound = null;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "api.test") { outbound = req; return Json("""{"ok":true}"""); }
            return RouteFindThing(req, thingId, "EP") ?? RouteEffectiveProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        outbound!.Headers.Contains("Accept").Should().BeFalse();
    }

    [Fact]
    public async Task Handle_AcceptHeaderWithBinaryKind_AppliesToFetchAndEnvelopes()
    {
        // The ImageServer scenario: acceptHeader negotiates the format, responseKind=binary
        // carries the bytes home — the two keys must compose.
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":          {"Value":"https://tiles.test/exportImage"},
          "Endpoint.httpMethod":   {"Value":"GET"},
          "Endpoint.responseKind": {"Value":"binary"},
          "Endpoint.acceptHeader": {"Value":"image/tiff"}
        }
        """;
        HttpRequestMessage? outbound = null;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "tiles.test") { outbound = req; return Binary(TiffBytes, "image/tiff"); }
            return RouteFindThing(req, thingId, "EP") ?? RouteEffectiveProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        outbound!.Headers.TryGetValues("Accept", out var values).Should().BeTrue();
        values!.Should().ContainSingle().Which.Should().Be("image/tiff");
        using var envelope = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        envelope.RootElement.GetProperty("contentType").GetString().Should().Be("image/tiff");
        Convert.FromBase64String(envelope.RootElement.GetProperty("dataBase64").GetString()!)
            .Should().Equal(TiffBytes);
    }

    [Fact]
    public async Task Handle_AmbiguousAcceptHeader_Returns400WithConflicts()
    {
        // Two acceptHeader declarations from divergent template branches — the resolver must
        // surface the conflict, mirroring every other structural key.
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":              {"Value":"https://api.test/data"},
          "Endpoint.httpMethod":       {"Value":"GET"},
          "EsriExport.acceptHeader":   {"Value":"image/tiff"},
          "GeoJsonFeed.acceptHeader":  {"Value":"application/geo+json"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
            RouteFindThing(req, thingId, "EP") ?? RouteEffectiveProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("acceptHeader").And.Contain("EsriExport.acceptHeader").And.Contain("GeoJsonFeed.acceptHeader");
    }
}
