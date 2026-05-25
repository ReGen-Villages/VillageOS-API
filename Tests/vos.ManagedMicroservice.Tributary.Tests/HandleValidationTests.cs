using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Xunit;

namespace vos.ManagedMicroservice.Tributary.Tests;

/// <summary>
/// Integration tests for Tributary's <c>/handle</c> validation surface:
/// <list type="bullet">
///   <item>Empty-string url in the effective properties returns 400 with the documented "non-empty strings" message.</item>
///   <item>Effective properties exposing <c>url</c>/<c>httpMethod</c> as raw keys (no <c>Endpoint.</c> prefix) route through <c>EffectivePropertyResolver</c>'s exact-match branch and complete the happy path.</item>
/// </list>
/// <c>EffectivePropertyResolver</c> itself is unit-tested in
/// <c>Helpers/EffectivePropertyResolverTests.cs</c>; these tests pin the end-to-end
/// integration through Program.cs.
/// </summary>
public class HandleValidationTests
{
    [Fact]
    public async Task Handle_EmptyUrlString_Returns400()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":""},
          "Endpoint.httpMethod": {"Value":"GET"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/things"
                && req.RequestUri.Query.Contains("name=EP"))
                return Json($$"""{"Id":"{{thingId}}","Name":"EP"}""");
            if (req.RequestUri.AbsolutePath == $"/api/things/{thingId}/effective-properties")
                return Json(props);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("non-empty strings");
    }

    [Fact]
    public async Task Handle_RawUrlAndMethodKeys_HappyPath_DispatchesEndpoint()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "url":        {"Value":"https://api.test/raw"},
          "httpMethod": {"Value":"GET"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/things"
                && req.RequestUri.Query.Contains("name=EP"))
                return Json($$"""{"Id":"{{thingId}}","Name":"EP"}""");
            if (req.RequestUri.AbsolutePath == $"/api/things/{thingId}/effective-properties")
                return Json(props);
            if (req.RequestUri.Host == "api.test")
                return Json("{\"value\":1}");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("{\"value\":1}");
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
}
