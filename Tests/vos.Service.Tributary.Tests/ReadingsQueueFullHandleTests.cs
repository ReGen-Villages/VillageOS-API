using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using vos.Service.Shared;
using vos.Service.Tributary.Services;
using vos.Tests.Shared;
using Xunit;
using static vos.Service.Tributary.Tests.MyceliumStub;

namespace vos.Service.Tributary.Tests;

public class ReadingsQueueFullHandleTests
{
    private static readonly byte[] PngBytes = { 0x89, 0x50, 0x4E, 0x47 };
    private const string Ticket = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Handle_CallGivenUpWhileTheReadingsQueueIsFull_StopsSendingTheBatch()
    {
        var thingId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":               {"Value":"https://api.test/point"},
          "Endpoint.httpMethod":        {"Value":"GET"},
          "Endpoint.responseTransform": {"Value":"{\"properties\": {\"flowRegime\": regime}}"}
        }
        """;
        using var callGivenUp = new CancellationTokenSource();
        var writes = 0;

        await AssertTheBatchIsSentOnceAsync(callGivenUp, () => writes, new { endpointName = "EP", subjectId }, req =>
        {
            if (IsObservationWrite(req, subjectId))
            {
                Interlocked.Increment(ref writes);
                callGivenUp.Cancel();
                return ReadingsQueueFull("1");
            }
            if (req.RequestUri!.Host == "api.test")
                return Json("""{"regime":"steady"}""");
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
                ?? RouteRelationshipWrite(req)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_ModelAssetCallGivenUpWhileTheReadingsQueueIsFull_StopsSendingTheTicket(bool binaryResponse)
    {
        var thingId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":           {"Value":"https://tiles.test/tile/1/0/1"},
          "Endpoint.httpMethod":    {"Value":"GET"},
          "Endpoint.assetProperty": {"Value":"surfaceMap"},
          "Endpoint.assetSubject":  {"Value":"SiteAlpha"}
        }
        """;
        var keeping = new Kind(EndpointKindRoles.Keeping, "ModelAsset", "assetProperty", "assetSubject");
        Kind[] kinds = binaryResponse ? [new Kind(EndpointKindRoles.ResponseBody, "BinaryResponse"), keeping] : [keeping];
        using var callGivenUp = new CancellationTokenSource();
        var writes = 0;

        await AssertTheBatchIsSentOnceAsync(callGivenUp, () => writes, new { endpointName = "EP" }, req =>
        {
            if (IsObservationWrite(req, subjectId))
            {
                Interlocked.Increment(ref writes);
                callGivenUp.Cancel();
                return ReadingsQueueFull("1");
            }
            if (req.RequestUri!.Host == "tiles.test")
                return binaryResponse ? Binary(PngBytes, "image/png") : Json("""{"n":1}""");
            return RouteFindThing(req, thingId, "EP") ?? RouteFindThing(req, subjectId, "SiteAlpha")
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKinds(req, thingId, kinds)
                ?? RouteAssetDeposit(req, Ticket)
                ?? RouteRelationshipWrite(req)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        });
    }

    private static bool IsObservationWrite(HttpRequestMessage request, Guid thingId) =>
        request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == $"/api/things/{thingId}/observations";

    // The clock fires every wait at once, so a wait the cancellation did not reach sends the batch again
    // straight away, where this test sees it.
    private static async Task AssertTheBatchIsSentOnceAsync(
        CancellationTokenSource callGivenUp, Func<int> writes, object handleRequest,
        Func<HttpRequestMessage, HttpResponseMessage> broker)
    {
        var log = new CapturingLogger<MyceliumClient>();
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = broker;
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(new ClockThatWaitsNoTime());
            services.AddSingleton<ILogger<MyceliumClient>>(log);
        }));
        using var client = host.CreateClient();

        var send = () => client.PostAsJsonAsync("/handle", handleRequest, callGivenUp.Token);

        await send.Should().ThrowAsync<OperationCanceledException>();
        await Settle.UntilAsync(() => log.Lines.Any(line => line.Contains("Dropped")),
            "the client gives the batch up one way or another");
        log.Lines.Should().ContainSingle(line => line.Contains("Dropped") && line.Contains("given up"));
        writes().Should().Be(1);
    }
}
