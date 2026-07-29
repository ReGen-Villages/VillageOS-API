using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using vos.ManagedMicroservice.ModelBridge.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.ManagedMicroservice.ModelBridge.Tests;

// Story #5866 — the model⇄DAG bridge: read outputs a Thing's property value, write persists its input onto one.
public class ModelBridgeNodeTests
{
    private static readonly Guid ThingId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class RecordingHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest;
        public string? LastBody;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return responder(request, LastBody ?? "");
        }
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static ModelBridgeNode NewNode(RecordingHandler handler) =>
        new(new TestHttpClientFactory(new HttpClient(handler)), NullLogger<ModelBridgeNode>.Instance,
            "http://mycelium", serviceToken: "test-token");

    private static JsonElement Envelope(string mode, string property, string? inputs = null)
    {
        var inputsJson = inputs ?? "{}";
        var json = $$"""
            { "runId": "{{Guid.NewGuid()}}", "nodeId": "{{Guid.NewGuid()}}",
              "params": { "mode": "{{mode}}", "thingId": "{{ThingId}}", "property": "{{property}}" },
              "inputs": {{inputsJson}} }
            """;
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    [Fact]
    public async Task Read_outputs_the_property_value()
    {
        var handler = new RecordingHandler((_, _) => Json("""{ "total_pv_area": { "Value": 13842.0 } }"""));
        var response = await NewNode(handler).HandleNodeAsync(Envelope("read", "total_pv_area"));

        Assert.True(response.Success);
        Assert.Equal(13842.0, Assert.IsType<double>(response.Outputs["value"]));
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Contains($"/api/things/{ThingId}/properties", handler.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task Write_posts_the_input_value_as_a_fact()
    {
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        var response = await NewNode(handler).HandleNodeAsync(Envelope("write", "pctOfConsumption", inputs: """{ "value": 120 }"""));

        Assert.True(response.Success);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("/properties/pctOfConsumption/facts", handler.LastRequest.RequestUri!.ToString());
        Assert.Contains("120", handler.LastBody);
    }

    [Fact]
    public async Task Unknown_mode_fails_the_node_not_a_500()
    {
        var response = await NewNode(new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)))
            .HandleNodeAsync(Envelope("bogus", "x"));

        Assert.False(response.Success);
        Assert.Contains("mode", response.Error);
    }

    [Fact]
    public async Task Read_of_a_missing_property_fails_the_node()
    {
        var handler = new RecordingHandler((_, _) => Json("""{ "other": { "Value": 1 } }"""));
        var response = await NewNode(handler).HandleNodeAsync(Envelope("read", "total_pv_area"));

        Assert.False(response.Success);
        Assert.Contains("total_pv_area", response.Error);
    }
}
