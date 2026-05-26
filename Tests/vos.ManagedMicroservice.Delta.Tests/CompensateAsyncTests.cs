using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Xunit;

namespace vos.ManagedMicroservice.Delta.Tests;

/// <summary>
/// Integration test for Delta's compensation behavior: when a registered Endpoint thing
/// gets stuck mid-registration (e.g. <c>SetThingPropertyAsync</c> fails after the thing
/// is created), the handler MUST issue a DELETE for the orphaned thing &mdash; even if the
/// DELETE itself fails (in which case the compensation logs an error but the request
/// still returns 500).
///
/// Pins the contract: <c>deleteAttempted.Should().BeTrue()</c> is the real assertion.
/// A regression that silently skips compensation would leak orphans in the broker model.
/// </summary>
public class CompensateAsyncTests
{
    [Fact]
    public async Task Compensate_DeleteAlsoFails_StillAttemptsDelete()
    {
        await using var factory = new DeltaWebApplicationFactory();
        await factory.InitializeAsync();

        var isId = Guid.NewGuid();
        var defaultId = Guid.NewGuid();
        var registeredId = Guid.NewGuid();
        var deleteAttempted = false;

        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/things" && req.Method == HttpMethod.Get
                && req.RequestUri.Query.Contains("name=is"))
                return Json("{\"Id\":\"" + isId + "\",\"Name\":\"is\",\"Properties\":{}}");
            if (req.RequestUri.AbsolutePath == "/api/things" && req.Method == HttpMethod.Get
                && req.RequestUri.Query.Contains("name=Endpoint"))
                return Json("{\"Id\":\"" + defaultId + "\",\"Name\":\"Endpoint\",\"Properties\":{}}");
            if (req.RequestUri.AbsolutePath == "/api/things" && req.Method == HttpMethod.Post)
                return Json("{\"Id\":\"" + registeredId + "\",\"Name\":\"MyEndpoint\",\"Properties\":{}}");
            if (req.RequestUri.AbsolutePath == "/api/relationships" && req.Method == HttpMethod.Post)
                return new HttpResponseMessage(HttpStatusCode.Created);
            if (req.RequestUri.AbsolutePath == $"/api/things/{registeredId}/properties" && req.Method == HttpMethod.Put)
                return new HttpResponseMessage(HttpStatusCode.BadRequest);   // triggers compensation
            if (req.RequestUri.AbsolutePath == $"/api/things/{registeredId}" && req.Method == HttpMethod.Delete)
            {
                deleteAttempted = true;
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);   // compensation fails too
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle",
            new { name = "MyEndpoint", properties = new { url = "https://x", httpMethod = "GET" } });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        deleteAttempted.Should().BeTrue("compensation must attempt delete even when it will fail");
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
}
