// Endpoint-level tests for Metabolism's MapMetabolismEndpoints via WebApplicationFactory<Program>,
// per the canonical pattern in docs/MICROSERVICES.md §10. Phase 2B landed first; see
// docs/TEST-STATE.md > "WebApplicationFactory<Program>..." for the shape rationale.
// Sibling reference: vos.Mycelium.Tests.MyceliumWebApplicationFactory.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace vos.ManagedMicroservice.Metabolism.Tests;

public class EndpointMapperTests : IAsyncLifetime
{
    private MetabolismWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new MetabolismWebApplicationFactory();
        await _factory.InitializeAsync();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    // ---- GET /health ----

    [Fact]
    public async Task Health_ReturnsHealthyEnvelope()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("Healthy");
        body.GetProperty("service").GetString().Should().Be("Metabolism-consumes");
        body.GetProperty("requestsProcessed").GetInt32().Should().Be(0);
        body.GetProperty("activeSimulations").GetInt32().Should().Be(0);
        body.GetProperty("totalSimulations").GetInt32().Should().Be(0);
        body.GetProperty("uptime").GetString().Should().Be("active");
    }

    // ---- GET /stats ----

    [Fact]
    public async Task Stats_ReturnsServiceMetadata()
    {
        var response = await _client.GetAsync("/stats");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("service").GetString().Should().Be("Metabolism-consumes");
        body.GetProperty("version").GetString().Should().Be("2.0.0");
        body.GetProperty("requestsProcessed").GetInt32().Should().Be(0);
        body.GetProperty("myceliumUrl").GetString().Should().Be("http://localhost:1");
        body.TryGetProperty("handlerId", out _).Should().BeTrue();
    }

    // ---- POST /handle ----

    [Fact]
    public async Task Handle_ValidRequest_RegistersSimulation_IncrementsCount()
    {
        var rel = Guid.NewGuid().ToString();
        var subject = Guid.NewGuid().ToString();
        var target = Guid.NewGuid().ToString();

        var response = await _client.PostAsJsonAsync("/handle", new
        {
            relationshipId = rel,
            subjectId = subject,
            targetId = target,
            // HandleRequest.Properties is a nested JsonElement; simulation fields live inside it.
            properties = new
            {
                quantity = 1.5m,
                unit = "kg",
                propertyPath = "weight",
                frequencySeconds = 60,
                startDelaySeconds = 0m,
                startUtc = DateTime.UtcNow.ToString("o"),
                endUtc = DateTime.UtcNow.AddHours(1).ToString("o")
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("success").GetBoolean().Should().BeTrue();
        body.GetProperty("registered").GetBoolean().Should().BeTrue();
        body.GetProperty("relationshipId").GetString().Should().Be(rel);
        body.GetProperty("mode").GetString().Should().Be("consumes");
        body.GetProperty("quantity").GetDecimal().Should().Be(1.5m);
        body.GetProperty("unit").GetString().Should().Be("kg");

        // /stats should now show requestsProcessed=1 (the incrementRequestCount call on /handle)
        var stats = await _client.GetFromJsonAsync<JsonElement>("/stats");
        stats.GetProperty("requestsProcessed").GetInt32().Should().Be(1);

        // /simulations should list the registered entry
        var sims = await _client.GetFromJsonAsync<JsonElement>("/simulations");
        sims.ValueKind.Should().Be(JsonValueKind.Array);
        sims.GetArrayLength().Should().Be(1);
        sims[0].GetProperty("relationshipId").GetString().Should().Be(rel);
    }

    [Fact]
    public async Task Handle_MalformedJson_Returns400()
    {
        // Body that doesn't deserialize into HandleRequest at all → ASP.NET returns 400
        // before the lambda runs. Sanity check that the route is hooked correctly.
        var response = await _client.PostAsync(
            "/handle",
            new StringContent("{not json", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Handle_MissingSubjectAndTargetIds_ReturnsBadRequestFromContractMiddleware()
    {
        // After Task #5429 wired UseRequestContractValidation() into Metabolism, empty
        // subjectId/targetId trip the schema's minLength:1 rule and the middleware rejects
        // the request before HandleRequestProcessor runs. Response is the contract envelope
        // {schemaId, errors[]} rather than the legacy {error} from the processor.
        var response = await _client.PostAsJsonAsync("/handle", new
        {
            relationshipId = Guid.NewGuid().ToString(),
            subjectId = "",
            targetId = "",
            properties = (object?)null
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.EnumerateArray().Should().Contain(e =>
            e.GetProperty("path").GetString()!.Contains("subjectId"));
    }

    // ---- GET /simulations ----

    [Fact]
    public async Task Simulations_NoneRegistered_ReturnsEmptyArray()
    {
        var response = await _client.GetAsync("/simulations");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Array);
        body.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Simulations_AfterRegistration_ListsAllExpectedFields()
    {
        var rel = Guid.NewGuid();
        await _client.PostAsJsonAsync("/handle", ValidHandleBody(rel));

        var response = await _client.GetAsync("/simulations");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetArrayLength().Should().Be(1);
        var sim = body[0];
        sim.GetProperty("relationshipId").GetString().Should().Be(rel.ToString());
        sim.TryGetProperty("subjectId", out _).Should().BeTrue();
        sim.TryGetProperty("targetId", out _).Should().BeTrue();
        sim.TryGetProperty("quantity", out _).Should().BeTrue();
        sim.TryGetProperty("unit", out _).Should().BeTrue();
        sim.TryGetProperty("propertyPath", out _).Should().BeTrue();
        sim.TryGetProperty("frequencySeconds", out _).Should().BeTrue();
        sim.TryGetProperty("startDelaySeconds", out _).Should().BeTrue();
        sim.TryGetProperty("startUtc", out _).Should().BeTrue();
        sim.TryGetProperty("endUtc", out _).Should().BeTrue();
        sim.TryGetProperty("status", out _).Should().BeTrue();
        sim.TryGetProperty("tickCount", out _).Should().BeTrue();
        sim.TryGetProperty("registeredAt", out _).Should().BeTrue();
    }

    // ---- DELETE /simulations/{relationshipId} ----

    [Fact]
    public async Task DeleteSimulation_ExistingRelationship_ReturnsOk_AndCancels()
    {
        var rel = Guid.NewGuid();
        await _client.PostAsJsonAsync("/handle", ValidHandleBody(rel));

        var response = await _client.DeleteAsync($"/simulations/{rel}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("message").GetString().Should().Contain(rel.ToString());
        body.GetProperty("message").GetString().Should().Contain("cancelled");

        // The simulation should now be gone
        var sims = await _client.GetFromJsonAsync<JsonElement>("/simulations");
        sims.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task DeleteSimulation_UnknownRelationship_ReturnsNotFoundWithError()
    {
        var unknown = Guid.NewGuid();

        var response = await _client.DeleteAsync($"/simulations/{unknown}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Contain(unknown.ToString());
    }

    // ---- POST /shutdown ----

    [Fact]
    public async Task Shutdown_ReturnsMessage_AndSchedulesAppStop()
    {
        var response = await _client.PostAsync("/shutdown", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("message").GetString().Should().Contain("consumes");
        body.GetProperty("message").GetString().Should().Contain("Shutting down");
        // The shutdown is fire-and-forget via Task.Run with a 300ms delay; we don't wait for
        // it (the test factory will dispose the host anyway). Just pin the immediate response.
    }

    // ---- helpers ----

    private static object ValidHandleBody(Guid relationshipId) => new
    {
        relationshipId = relationshipId.ToString(),
        subjectId = Guid.NewGuid().ToString(),
        targetId = Guid.NewGuid().ToString(),
        properties = new
        {
            quantity = 1m,
            unit = "kg",
            propertyPath = "weight",
            frequencySeconds = 60,
            startDelaySeconds = 0m,
            startUtc = DateTime.UtcNow.ToString("o"),
            endUtc = DateTime.UtcNow.AddHours(1).ToString("o")
        }
    };
}
