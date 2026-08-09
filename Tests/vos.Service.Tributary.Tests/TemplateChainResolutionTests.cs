using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using static vos.Service.Tributary.Tests.MyceliumStub;

namespace vos.Service.Tributary.Tests;

// A root template declares a key's default and a descendant overrides it (#5914's shape:
// responseKind on root Endpoint, narrowed by EsriTileEndpoint). Pinned through /handle because
// Mycelium's effective-property view is where the two declarations both show up.
public class TemplateChainResolutionTests
{
    private static readonly byte[] PngBytes = { 0x89, 0x50, 0x4E, 0x47, 0xFF, 0xFE, 0x00, 0x01 };

    [Fact]
    public async Task Handle_RootDefaultOverriddenOnChain_ResolvesClosestAndSucceeds()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "EsriTileEndpoint.Endpoint.httpMethod":   {"Value":"GET"},
          "EsriTileEndpoint.Endpoint.responseKind": {"Value":"json"},
          "EsriTileEndpoint.httpMethod":            {"Value":"GET"},
          "EsriTileEndpoint.responseKind":          {"Value":"binary"},
          "url":                                    {"Value":"https://tiles.test/tile/0/0/0"}
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

        // A base64 envelope rather than a JSON body is the proof that responseKind resolved to the
        // child's binary rather than the root's json.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var envelope = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        envelope.RootElement.GetProperty("contentType").GetString().Should().Be("image/png");
        Convert.FromBase64String(envelope.RootElement.GetProperty("dataBase64").GetString()!)
            .Should().Equal(PngBytes);
    }
}
