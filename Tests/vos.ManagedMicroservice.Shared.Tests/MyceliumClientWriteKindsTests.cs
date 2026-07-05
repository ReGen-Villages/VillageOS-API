// Contract tests for the three write-kind helpers on MyceliumClientBase: wire shape, success codes,
// the StatusCode-carrying failure, and that the embedded schemas accept the real payloads (Throw mode).

using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.ManagedMicroservice.Shared.Contracts.Validation;
using vos.Tests.Shared;
using Xunit;

namespace vos.ManagedMicroservice.Shared.Tests;

public class MyceliumClientWriteKindsTests
{
    private const string MyceliumUrl = "http://localhost:7243";
    private const string ServiceToken = "service-token-abc";
    private static readonly Guid Thing = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime At = new(2026, 6, 20, 14, 0, 0, DateTimeKind.Utc);

    // ---------------- Facts ----------------

    [Fact]
    public async Task SetFactAsync_Created_PostsValueAndReturnsSequence()
    {
        HttpRequestMessage? captured = null;
        var (client, _) = BuildClient(req => { captured = req; return Json(HttpStatusCode.Created, """{"sequenceNumber":42,"value":"active"}"""); });

        var seq = await client.SetFactAsync(Thing, "status", "active");

        seq.Should().Be(42);
        captured!.Method.Should().Be(HttpMethod.Post);
        captured.RequestUri!.AbsoluteUri.Should().Be($"{MyceliumUrl}/api/things/{Thing}/properties/status/facts");
        Body(captured).GetProperty("value").GetString().Should().Be("active");
    }

    [Fact]
    public async Task SetFactAsync_ObservationOnly_405_ThrowsWithStatus()
    {
        var (client, _) = BuildClient(_ => Json(HttpStatusCode.MethodNotAllowed, """{"error":"property is ObservationOnly"}"""));

        var act = () => client.SetFactAsync(Thing, "temperature", 21.5);

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    // ---------------- Observations (single) ----------------

    [Fact]
    public async Task RecordObservationAsync_WithObservedAt_PostsValueAndTime()
    {
        HttpRequestMessage? captured = null;
        var (client, _) = BuildClient(req => { captured = req; return new HttpResponseMessage(HttpStatusCode.Accepted); });

        await client.RecordObservationAsync(Thing, "temperature", 21.5, At);

        captured!.RequestUri!.AbsoluteUri.Should().Be($"{MyceliumUrl}/api/things/{Thing}/properties/temperature/observations");
        var body = Body(captured);
        body.GetProperty("value").GetDouble().Should().Be(21.5);
        body.TryGetProperty("observedAt", out _).Should().BeTrue();
    }

    [Fact]
    public async Task RecordObservationAsync_NoObservedAt_OmitsTimeField()
    {
        HttpRequestMessage? captured = null;
        var (client, _) = BuildClient(req => { captured = req; return new HttpResponseMessage(HttpStatusCode.Accepted); });

        await client.RecordObservationAsync(Thing, "temperature", 21.5);

        Body(captured!).TryGetProperty("observedAt", out _).Should().BeFalse();
    }

    [Fact]
    public async Task RecordObservationAsync_FactOnly_405_ThrowsWithStatus()
    {
        var (client, _) = BuildClient(_ => Json(HttpStatusCode.MethodNotAllowed, """{"error":"property is FactOnly"}"""));

        var act = () => client.RecordObservationAsync(Thing, "status", "x");

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    // ---------------- Observations (batch) ----------------

    [Fact]
    public async Task RecordObservationsAsync_Batch_PostsArrayAndReturnsAccepted()
    {
        HttpRequestMessage? captured = null;
        var (client, _) = BuildClient(req => { captured = req; return Json(HttpStatusCode.Accepted, """{"accepted":2}"""); });

        var accepted = await client.RecordObservationsAsync(Thing, new[]
        {
            new ObservationSample("temperature", 21.5),
            new ObservationSample("flow", 3.1, At),
        });

        accepted.Should().Be(2);
        captured!.RequestUri!.AbsoluteUri.Should().Be($"{MyceliumUrl}/api/things/{Thing}/observations");
        var arr = Body(captured);
        arr.GetArrayLength().Should().Be(2);
        arr[0].GetProperty("property").GetString().Should().Be("temperature");
        arr[1].TryGetProperty("observedAt", out _).Should().BeTrue();
    }

    [Fact]
    public async Task RecordObservationsAsync_Empty_ReturnsZeroWithoutCalling()
    {
        var (client, handler) = BuildClient(_ => throw new InvalidOperationException("Mycelium should not be called for an empty batch"));

        var accepted = await client.RecordObservationsAsync(Thing, Array.Empty<ObservationSample>());

        accepted.Should().Be(0);
        handler.Requests.Should().BeEmpty();
    }

    // ---------------- Sediment ----------------

    [Fact]
    public async Task DepositSedimentAsync_Accepted_PostsReadingsAndReturnsSummary()
    {
        var batchId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        HttpRequestMessage? captured = null;
        var (client, _) = BuildClient(req =>
        {
            captured = req;
            return Json(HttpStatusCode.Accepted, JsonSerializer.Serialize(new { batchId, series = 1, buckets = 3, samples = 10 }));
        });

        var result = await client.DepositSedimentAsync(new[]
        {
            new SedimentReading(Thing, "flow", 1.0, At),
            new SedimentReading(Thing, "flow", 2.0, At.AddMinutes(5)),
        });

        result.Should().Be(new SedimentDepositResult(batchId, 1, 3, 10));
        captured!.RequestUri!.AbsoluteUri.Should().Be($"{MyceliumUrl}/api/sediment");
        var arr = Body(captured);
        arr.GetArrayLength().Should().Be(2);
        arr[0].GetProperty("objectId").GetGuid().Should().Be(Thing);
        arr[0].GetProperty("property").GetString().Should().Be("flow");
        arr[0].TryGetProperty("observedAt", out _).Should().BeTrue(); // sediment always carries observed-time
    }

    [Fact]
    public async Task DepositSedimentAsync_Empty_ThrowsArgumentException()
    {
        var (client, handler) = BuildClient(_ => throw new InvalidOperationException("should not be called"));

        var act = () => client.DepositSedimentAsync(Array.Empty<SedimentReading>());

        await act.Should().ThrowAsync<ArgumentException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task DepositSedimentAsync_ThingNotFound_404_ThrowsWithStatus()
    {
        var (client, _) = BuildClient(_ => Json(HttpStatusCode.NotFound, """{"error":"Thing ... not found"}"""));

        var act = () => client.DepositSedimentAsync(new[] { new SedimentReading(Thing, "flow", 1.0, At) });

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------------- helpers ----------------

    private static (TestableMyceliumClient client, MockHttpMessageHandler handler) BuildClient(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new MockHttpMessageHandler(respond);
        var http = new HttpClient(handler);
        var factory = new TestHttpClientFactory(http);
        var client = new TestableMyceliumClient(factory, NullLogger.Instance, MyceliumUrl, ServiceToken)
        {
            ViolationModeForTests = SchemaViolationMode.Throw, // schemas must accept the real payloads
        };
        return (client, handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static JsonElement Body(HttpRequestMessage req)
    {
        var text = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
