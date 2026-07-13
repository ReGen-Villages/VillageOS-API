using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Xunit;

namespace vos.ManagedMicroservice.Delta.Tests;

// Integration test for Delta's case-insensitive property-name lookup at the /handle
// endpoint. JsonValueCoercion.TryGetPropertyValue is unit-tested directly (see
// Helpers/JsonValueCoercionTests.cs); this test exists to pin the end-to-end
// happy path — a caller posts properties with uppercase keys (URL, HTTPMethod) and
// the registration succeeds. A regression that broke the wiring (e.g. swapping the
// helper for an exact-match lookup) would unit-test pass but fail here.
public class PropertyLookupTests
{
    [Fact]
    public async Task Handle_CaseInsensitivePropertyKey_HappyPath_RegistersEndpoint()
    {
        await using var factory = new DeltaWebApplicationFactory();
        await factory.InitializeAsync();

        var isId = Guid.NewGuid();
        var defaultId = Guid.NewGuid();
        var registeredId = Guid.NewGuid();

        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/things" && req.Method == HttpMethod.Get
                && req.RequestUri.Query.Contains("name=is"))
                return Json("{\"Id\":\"" + isId + "\",\"Name\":\"is\",\"Properties\":{}}");
            if (req.RequestUri.AbsolutePath == "/api/things" && req.Method == HttpMethod.Get
                && req.RequestUri.Query.Contains("name=Endpoint"))
                return Json("{\"Id\":\"" + defaultId + "\",\"Name\":\"Endpoint\",\"Properties\":{}}");
            if (req.RequestUri.AbsolutePath == "/api/things" && req.Method == HttpMethod.Post)
                return Json("{\"Id\":\"" + registeredId + "\",\"Name\":\"EP\",\"Properties\":{}}");
            if (req.RequestUri.AbsolutePath == "/api/relationships" && req.Method == HttpMethod.Post)
                return new HttpResponseMessage(HttpStatusCode.Created);
            if (req.RequestUri.AbsolutePath == $"/api/things/{registeredId}/properties" && req.Method == HttpMethod.Put)
                return new HttpResponseMessage(HttpStatusCode.OK);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            name = "EP",
            properties = new Dictionary<string, object>
            {
                ["URL"] = "https://x.example/",       // uppercase
                ["HTTPMethod"] = "GET"                  // mixed case
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
}
