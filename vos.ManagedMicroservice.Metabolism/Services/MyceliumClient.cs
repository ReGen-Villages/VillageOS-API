using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using vos.ManagedMicroservice.Shared;

namespace vos.ManagedMicroservice.Metabolism.Services;

/// <summary>
/// HTTP client for communicating with the VOS Mycelium (Metabolism-specific operations).
/// Adds the resource/quantity write endpoints on top of the shared base. Live property
/// updates now arrive via the SSE <see cref="Shared.Subscriptions.SubscriptionClient"/>
/// (Phase 5c, #5558).
/// </summary>
public class MyceliumClient : MyceliumClientBase
{
    private readonly string _mode;

    public MyceliumClient(IHttpClientFactory httpClientFactory, ILogger<MyceliumClient> logger, string myceliumUrl, string mode, string? serviceToken = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken)
    {
        _mode = mode;
    }

    /// <summary>Registers this handler with Mycelium.</summary>
    public Task<bool> RegisterAsync(int port)
        => RegisterAsync(port, $"Metabolism-{_mode}",
            $"dotnet run --project vos.ManagedMicroservice.Metabolism -- --port={port} --myceliumUrl={MyceliumUrl} --mode={_mode}");

    private const string ApplyQuantitySchemaId = "https://villageos/contracts/apply-quantity-request.schema.json";
    private const string RelationshipIncrementSchemaId = "https://villageos/contracts/relationship-property-increment-request.schema.json";

    // Payload shape lives in a virtual builder so tests can inject a malformed object to
    // exercise the validation paths. Default returns the production wire shape.
    protected virtual object BuildApplyQuantityPayload(decimal amount, string? subjectName, string? unit) =>
        new { amount, subjectName = subjectName ?? "", unit = unit ?? "" };

    protected virtual object BuildIncrementRelationshipPayload(decimal amount) =>
        new { amount };

    /// <summary>Applies the resource operation (increment or decrement) based on mode.</summary>
    public async Task<JsonElement?> ApplyQuantityAsync(string thingId, string propertyPath, decimal amount, string? subjectName = null, string? unit = null)
    {
        var action = _mode == "consumes" ? "decrements" : "increments";

        var payload = BuildApplyQuantityPayload(amount, subjectName, unit);
        var json = JsonSerializer.Serialize(payload);

        // Validate the outbound payload before any network call. Throw mode propagates
        // ContractValidationException; Log mode warns and lets the call through.
        ValidateOutbound(json, ApplyQuantitySchemaId);

        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(30));
        var response = await client.PostAsync(
            $"{MyceliumUrl}/api/things/{thingId}/properties/{propertyPath}/{action}",
            new StringContent(json, Encoding.UTF8, "application/json")
        );

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new KeyNotFoundException($"Thing or property not found: {error}");
        }
        else
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"POST {action} failed ({response.StatusCode}): {error}");
        }
    }

    /// <summary>Increment a property on a relationship (for tracking per-relationship cumulative totals).</summary>
    public async Task IncrementRelationshipPropertyAsync(string relationshipId, string propertyPath, decimal amount)
    {
        var payload = BuildIncrementRelationshipPayload(amount);
        var json = JsonSerializer.Serialize(payload);

        ValidateOutbound(json, RelationshipIncrementSchemaId);

        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(30));
        var response = await client.PostAsync(
            $"{MyceliumUrl}/api/relationships/{relationshipId}/properties/{propertyPath}/increments",
            new StringContent(json, Encoding.UTF8, "application/json")
        );

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Relationship increment failed ({response.StatusCode}): {error}");
        }
    }
}
