using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using static vos.Service.Tributary.Tests.MyceliumStub;

namespace vos.Service.Tributary.Tests;

// Integration tests for responseKind=binary through /handle: upstream bytes must survive losslessly
// inside the { contentType, dataBase64, byteLength } envelope, and config combos that presuppose a
// decodable string body (responseTransform, offset paging) must be rejected up front.
//
// Effective-property keys are namespaced (e.g. "Endpoint.responseKind") to mirror Mycelium's
// template-merged shape; EffectivePropertyResolver suffix-matches them.
public class BinaryResponseKindTests
{
    // PNG magic followed by 0xFF 0xFE — invalid UTF-8, so any string decode replaces them with
    // U+FFFD and the round-trip assertion below can only pass on a byte-level read.
    private static readonly byte[] PngBytes = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xFF, 0xFE, 0x00, 0x01 };

    [Fact]
    public async Task Handle_ResponseKindBinary_ReturnsBase64EnvelopeWithExactBytes()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":          {"Value":"https://tiles.test/tile/0/0/0"},
          "Endpoint.httpMethod":   {"Value":"GET"},
          "Endpoint.responseKind": {"Value":"binary"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "tiles.test") return Binary(PngBytes, "image/png");
            return RouteFindThing(req, thingId, "EP") ?? RouteEffectiveProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        using var envelope = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        envelope.RootElement.GetProperty("contentType").GetString().Should().Be("image/png");
        envelope.RootElement.GetProperty("byteLength").GetInt32().Should().Be(PngBytes.Length);
        Convert.FromBase64String(envelope.RootElement.GetProperty("dataBase64").GetString()!)
            .Should().Equal(PngBytes);
    }

    [Fact]
    public async Task Handle_ResponseKindBinary_MissingUpstreamContentType_FallsBackToOctetStream()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":          {"Value":"https://tiles.test/tile/0/0/0"},
          "Endpoint.httpMethod":   {"Value":"GET"},
          "Endpoint.responseKind": {"Value":"binary"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "tiles.test") return Binary(PngBytes, contentType: null);
            return RouteFindThing(req, thingId, "EP") ?? RouteEffectiveProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var envelope = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        envelope.RootElement.GetProperty("contentType").GetString().Should().Be("application/octet-stream");
    }

    [Fact]
    public async Task Handle_ResponseKindBinary_WithTokenExchange_AttachesTokenAndEnvelopes()
    {
        // authKind and responseKind are orthogonal structural keys — the ESRI tile scenario
        // combines both, so the envelope must not disturb the credential attach.
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":          {"Value":"https://tiles.test/tile/0/0/0"},
          "Endpoint.httpMethod":   {"Value":"GET"},
          "Endpoint.responseKind": {"Value":"binary"},
          "Endpoint.authKind":     {"Value":"tokenExchange"},
          "Esri.token":            {"Value":"PRE-MINTED"}
        }
        """;
        HttpRequestMessage? outbound = null;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "tiles.test") { outbound = req; return Binary(PngBytes, "image/jpeg"); }
            return RouteFindThing(req, thingId, "EP") ?? RouteEffectiveProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        outbound!.RequestUri!.Query.Should().Contain("token=PRE-MINTED");
        using var envelope = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        envelope.RootElement.GetProperty("contentType").GetString().Should().Be("image/jpeg");
        Convert.FromBase64String(envelope.RootElement.GetProperty("dataBase64").GetString()!)
            .Should().Equal(PngBytes);
    }

    [Fact]
    public async Task Handle_ResponseKindJsonExplicit_ReturnsRawBodyUnchanged()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":          {"Value":"https://features.test/query"},
          "Endpoint.httpMethod":   {"Value":"GET"},
          "Endpoint.responseKind": {"Value":"json"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "features.test") return Json("{\"ok\":true}");
            return RouteFindThing(req, thingId, "EP") ?? RouteEffectiveProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("{\"ok\":true}");
    }

    [Fact]
    public async Task Handle_UnsupportedResponseKind_Returns400()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":          {"Value":"https://features.test/query"},
          "Endpoint.httpMethod":   {"Value":"GET"},
          "Endpoint.responseKind": {"Value":"xml"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req => RouteFindThing(req, thingId, "EP")
            ?? RouteEffectiveProperties(req, thingId, props)
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Unsupported responseKind");
    }

    [Fact]
    public async Task Handle_AmbiguousResponseKind_Returns400()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://features.test/query"},
          "Endpoint.httpMethod": {"Value":"GET"},
          "A.responseKind":      {"Value":"json"},
          "B.responseKind":      {"Value":"binary"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req => RouteFindThing(req, thingId, "EP")
            ?? RouteEffectiveProperties(req, thingId, props)
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ambiguous properties for responseKind");
    }

    [Fact]
    public async Task Handle_ResponseKindBinary_WithTemplateTransform_Returns400()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":               {"Value":"https://tiles.test/tile/0/0/0"},
          "Endpoint.httpMethod":        {"Value":"GET"},
          "Endpoint.responseKind":      {"Value":"binary"},
          "Endpoint.responseTransform": {"Value":"$count(features)"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req => RouteFindThing(req, thingId, "EP")
            ?? RouteEffectiveProperties(req, thingId, props)
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("cannot be combined with responseTransform");
    }

    [Fact]
    public async Task Handle_ResponseKindBinary_WithOverrideTransform_Returns400WithoutPersistingOverride()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":          {"Value":"https://tiles.test/tile/0/0/0"},
          "Endpoint.httpMethod":   {"Value":"GET"},
          "Endpoint.responseKind": {"Value":"binary"}
        }
        """;
        var persistCalls = 0;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.Method == HttpMethod.Put && req.RequestUri!.AbsolutePath == $"/api/things/{thingId}/properties")
            {
                persistCalls++;
                return Json("{}");
            }
            return RouteFindThing(req, thingId, "EP") ?? RouteEffectiveProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP", responseTransform = "$count(features)" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("cannot be combined with responseTransform");
        persistCalls.Should().Be(0, "the combo must be rejected before the override persists to the endpoint thing");
    }

    [Fact]
    public async Task Handle_ResponseKindBinary_WithOffsetPaging_Returns400()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":          {"Value":"https://tiles.test/tile/0/0/0"},
          "Endpoint.httpMethod":   {"Value":"GET"},
          "Endpoint.responseKind": {"Value":"binary"},
          "Esri.pagingKind":       {"Value":"offset"},
          "Esri.offsetParam":      {"Value":"resultOffset"},
          "Esri.hasMorePath":      {"Value":"exceededTransferLimit"},
          "Esri.itemsPath":        {"Value":"features"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req => RouteFindThing(req, thingId, "EP")
            ?? RouteEffectiveProperties(req, thingId, props)
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("cannot be combined with pagingKind");
    }
}
