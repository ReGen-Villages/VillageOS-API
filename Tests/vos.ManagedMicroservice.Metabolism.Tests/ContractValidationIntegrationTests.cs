using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace vos.ManagedMicroservice.Metabolism.Tests;

// Integration tests covering the contract-validation middleware on Metabolism's /handle
// endpoint. Task #5429 wires AddContractValidation + UseRequestContractValidation
// + RequireContract<HandleRequest> into Program.cs. Tests use
// WebApplicationFactory<Program> (same pattern as EndpointMapperTests).
// Schema: handle-request-metabolism.schema.json — requires subjectId and
// targetId (both non-empty strings); additionalProperties: false.
public class ContractValidationIntegrationTests : IAsyncLifetime
{
    private const string SchemaId = "https://villageos/contracts/handle-request-metabolism.schema.json";

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

    [Fact]
    public async Task Handle_ValidRequest_PassesValidation_ReturnsOk()
    {
        var response = await _client.PostAsJsonAsync("/handle", new
        {
            relationshipId = Guid.NewGuid().ToString(),
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
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Handle_MissingSubjectId_Returns400WithRequiredError()
    {
        var response = await _client.PostAsJsonAsync("/handle", new
        {
            relationshipId = Guid.NewGuid().ToString(),
            targetId = Guid.NewGuid().ToString()
            // subjectId omitted
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("schemaId").GetString().Should().Be(SchemaId);
        var errors = body.GetProperty("errors");
        errors.GetArrayLength().Should().BeGreaterThan(0);
        errors.EnumerateArray().Should().Contain(e =>
            e.GetProperty("code").GetString() == "Required" &&
            e.GetProperty("path").GetString()!.Contains("subjectId"));
    }

    [Fact]
    public async Task Handle_UnknownProperty_Returns400WithAdditionalPropertiesError()
    {
        var response = await _client.PostAsJsonAsync("/handle", new
        {
            subjectId = Guid.NewGuid().ToString(),
            targetId = Guid.NewGuid().ToString(),
            bogusField = "not in schema"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("schemaId").GetString().Should().Be(SchemaId);
        body.GetProperty("errors").EnumerateArray().Should().Contain(e =>
            e.GetProperty("code").GetString() == "AdditionalProperties");
    }

    [Fact]
    public async Task Handle_WrongTypeOnSubjectId_Returns400WithTypeError()
    {
        // subjectId as number instead of string — must use raw JSON, anonymous object
        // would serialize as number which is exactly what we want here.
        var rawJson = """
            {
              "subjectId": 123,
              "targetId": "thing-b"
            }
            """;

        var response = await _client.PostAsync(
            "/handle",
            new StringContent(rawJson, System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("schemaId").GetString().Should().Be(SchemaId);
        body.GetProperty("errors").EnumerateArray().Should().Contain(e =>
            e.GetProperty("code").GetString() == "Type" &&
            e.GetProperty("path").GetString()!.Contains("subjectId"));
    }

    [Fact]
    public async Task Handle_MalformedJson_Returns400WithMalformedError()
    {
        var response = await _client.PostAsync(
            "/handle",
            new StringContent("{not json", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("schemaId").GetString().Should().Be(SchemaId);
        body.GetProperty("errors").EnumerateArray().Should().Contain(e =>
            e.GetProperty("code").GetString() == "Malformed");
    }

    [Fact]
    public async Task Health_HasNoContract_PassesThrough()
    {
        // /health has no [RequireContract] -- middleware must short-circuit and call next.
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("Healthy");
    }
}
