using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace vos.Service.Tributary.Tests;

// Integration pin for Bug #6152 through /handle: a root template may declare a key's default
// and a descendant may override it — the closest declaration must win instead of 400ing as
// ambiguous. This is the exact shape #5914 ships (responseKind declared on root Endpoint,
// overridden by EsriTileEndpoint).
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
            return RouteFindThing(req, thingId, "EP") ?? RouteEffectiveProps(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        // Closest ancestor won on both keys: httpMethod resolved (chain, identical values) and
        // responseKind=binary (child) shadowed the root's json default — hence an envelope.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var envelope = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        envelope.RootElement.GetProperty("contentType").GetString().Should().Be("image/png");
        Convert.FromBase64String(envelope.RootElement.GetProperty("dataBase64").GetString()!)
            .Should().Equal(PngBytes);
    }

    // ---------- helpers ----------

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Binary(byte[] bytes, string? contentType)
    {
        var content = new ByteArrayContent(bytes);
        if (contentType != null)
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static HttpResponseMessage? RouteFindThing(HttpRequestMessage req, Guid id, string name)
    {
        if (req.Method == HttpMethod.Get
            && req.RequestUri!.AbsolutePath == "/api/things"
            && req.RequestUri.Query.Contains($"name={name}"))
            return Json($$"""{"Id":"{{id}}","Name":"{{name}}"}""");
        return null;
    }

    private static HttpResponseMessage? RouteEffectiveProps(HttpRequestMessage req, Guid id, string jsonObject)
    {
        if (req.Method == HttpMethod.Get
            && req.RequestUri!.AbsolutePath == $"/api/things/{id}/properties")
            return Json(jsonObject);
        return null;
    }
}
