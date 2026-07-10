// Verifies WriteKindsDemo drives one of each write kind on the right routes and aggregates the result.

using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.ManagedMicroservice.CSharp.Echo.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.ManagedMicroservice.CSharp.Echo.Tests;

public class WriteKindsDemoTests
{
    private const string MyceliumUrl = "http://localhost:7243";
    private const string ServiceToken = "svc-jwt-abc";
    private static readonly Guid Thing = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Batch = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task RunAsync_DrivesAllThreeWriteKinds_AndAggregates()
    {
        var handler = new MockHttpMessageHandler(Respond);
        // The demo performs four writes; the real IHttpClientFactory hands out a fresh client per
        // call, so model that here (a singleton client can't have its Timeout reset after first use).
        var client = new MyceliumClient(new PerCallClientFactory(handler),
            NullLogger<MyceliumClient>.Instance, MyceliumUrl, ServiceToken);

        var result = await new WriteKindsDemo(client).RunAsync(Thing, new DateTime(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc));

        // One request per write kind, on the right route.
        var paths = handler.Requests.Select(r => r.RequestUri!.AbsolutePath).ToList();
        paths.Should().Contain($"/api/things/{Thing}/properties/status/facts");                 // Fact
        paths.Should().Contain($"/api/things/{Thing}/properties/temperature/observations");     // Observation (single)
        paths.Should().Contain($"/api/things/{Thing}/observations");                            // Observation (batch)
        paths.Should().Contain("/api/sediment");                                                // Sediment

        // Aggregated result reflects the mocked responses.
        result.FactSequence.Should().Be(7);
        result.ObservationsAccepted.Should().Be(3); // batch (2) + single (1)
        result.SedimentBatchId.Should().Be(Batch);
        result.SedimentSamples.Should().Be(2);
    }

    private static HttpResponseMessage Respond(HttpRequestMessage req)
    {
        var path = req.RequestUri!.AbsolutePath;
        if (path.EndsWith("/facts"))
            return Json(HttpStatusCode.Created, """{"sequenceNumber":7,"value":"active"}""");
        if (path.Contains("/properties/") && path.EndsWith("/observations"))
            return new HttpResponseMessage(HttpStatusCode.Accepted); // single observation
        if (path.EndsWith("/observations"))
            return Json(HttpStatusCode.Accepted, """{"accepted":2}"""); // batch
        if (path.EndsWith("/sediment"))
            return Json(HttpStatusCode.Accepted, JsonSerializer.Serialize(new { batchId = Batch, series = 1, buckets = 1, samples = 2 }));
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    // Returns a fresh HttpClient over the same recording handler on every call, matching how the
    // production IHttpClientFactory behaves (each CreateAuthenticatedClientAsync gets a new client).
    private sealed class PerCallClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
