using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace vos.ManagedMicroservice.Tributary.Tests;

// Tributary as a pipeline DAG node (Feature #5628, #5631): a node envelope {runId,nodeId,params,inputs}
// posted to /handle runs the SAME EndpointCallService path as a legacy call and returns {success,outputs,error}.
// The legacy endpoint-call behaviour is covered exhaustively by HandleEndpointTests; these pin the additive
// node path and the /manifest contract.
public class TributaryNodeHandleTests
{
    [Fact]
    public async Task NodeEnvelope_CallsEndpoint_AndReturnsResponseOutput()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://api.test/data"},
          "Endpoint.httpMethod": {"Value":"GET"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
            RouteFindThing(req, thingId, "EP")
            ?? RouteEffectiveProps(req, thingId, props)
            ?? (req.RequestUri!.Host == "api.test" && req.Method == HttpMethod.Get
                ? Json("{\"value\":42}")
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", NodeEnvelope(inputs: new { endpointName = "EP" }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("success").GetBoolean().Should().BeTrue();
        body.GetProperty("outputs").GetProperty("response").GetString().Should().Be("{\"value\":42}");
    }

    [Fact]
    public async Task NodeEnvelope_EndpointNotFound_ReturnsFailureNotError()
    {
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        // Mycelium GET /api/things?name=Unknown returns empty (non-object root → null thing)
        factory.HandlerCallback = req =>
            req.RequestUri!.AbsolutePath == "/api/things"
                ? Json("{}")
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", NodeEnvelope(inputs: new { endpointName = "Unknown" }));

        // The orchestrator reads node failure from the envelope, not an HTTP error.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("success").GetBoolean().Should().BeFalse();
        body.GetProperty("error").GetString().Should().Contain("not found");
    }

    [Fact]
    public async Task Manifest_AdvertisesEndpointNameInputAndResponseOutput()
    {
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/manifest");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var ports = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        ports.EnumerateArray().Should().Contain(p =>
            p.GetProperty("portName").GetString() == "endpointName"
            && p.GetProperty("direction").GetString() == "in"
            && p.GetProperty("required").GetBoolean());
        ports.EnumerateArray().Should().Contain(p =>
            p.GetProperty("portName").GetString() == "response"
            && p.GetProperty("direction").GetString() == "out");
    }

    private static object NodeEnvelope(object inputs) => new
    {
        runId = Guid.Parse("5628da90-0000-0000-0000-0000000000aa"),
        nodeId = Guid.Parse("5628da90-0000-0000-0000-0000000000bb"),
        @params = new { },
        inputs,
    };

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

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
            && req.RequestUri!.AbsolutePath == $"/api/things/{id}/effective-properties")
            return Json(jsonObject);
        return null;
    }
}
