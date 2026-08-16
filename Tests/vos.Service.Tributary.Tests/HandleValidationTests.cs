using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Xunit;

namespace vos.Service.Tributary.Tests;

// Integration tests for Tributary's /handle validation surface:
//   Empty-string url in the effective properties returns 400 with the documented "non-empty strings" message.
//   Effective properties exposing url/httpMethod as raw keys (no Endpoint. prefix) route through EffectivePropertyResolver's exact-match branch and complete the happy path.
// EffectivePropertyResolver itself is unit-tested in
// Helpers/EffectivePropertyResolverTests.cs; these tests pin the end-to-end
// integration through Program.cs.
public class HandleValidationTests
{
    // The gateway read that resolves an endpoint's kinds throws on any non-success status. Left
    // uncaught it escapes the whole call path as an exception rather than the 502 every other gateway
    // failure here produces — and worse, treating the failure as "reaches no kind" would send the call
    // out with no credential at all.
    [Fact]
    public async Task Handle_KindResolutionUnavailable_Returns502AndDoesNotCallOut()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://api.test/x"},
          "Endpoint.httpMethod": {"Value":"GET"}
        }
        """;
        var calledOut = false;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "api.test") { calledOut = true; return MyceliumStub.Json("{}"); }
            if (req.Method == HttpMethod.Post && req.RequestUri.AbsolutePath == "/api/subscriptions")
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            return MyceliumStub.RouteFindThing(req, thingId, "EP")
                ?? MyceliumStub.RouteEffectiveProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        calledOut.Should().BeFalse("an endpoint whose kinds could not be read must not be called unauthenticated");
    }

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
            if (req.RequestUri.AbsolutePath == $"/api/things/{thingId}/properties")
                return Json(props);
            if (MyceliumStub.RouteKinds(req, thingId) is { } kinds)
                return kinds;
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
            if (req.RequestUri.AbsolutePath == $"/api/things/{thingId}/properties")
                return Json(props);
            if (MyceliumStub.RouteKinds(req, thingId) is { } kinds)
                return kinds;
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
