using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using vos.ManagedMicroservice.Shared.Contracts.Validation;

namespace vos.ManagedMicroservice.Shared;

/// <summary>
/// Base class for microservice MyceliumClients that communicate with the VOS Mycelium.
/// Provides shared token management, registration, deregistration, and authenticated HTTP helpers.
/// Used by IsHandler, Metabolism, and Delta handler services.
/// </summary>
public abstract class MyceliumClientBase
{
    private const string MyceliumRegisterRequestSchemaId = "https://villageos/contracts/mycelium-register-request.schema.json";
    private const string TokenResponseSchemaId = "https://villageos/contracts/token-response.schema.json";

    // Schemas are eagerly loaded once per process. SchemaRegistry's ctor parses every embedded
    // schema and fails closed on duplicate/missing $id, so first access on a misconfigured
    // assembly throws -- but happens at most once.
    private static readonly Lazy<SchemaRegistry> _registry = new(() => new SchemaRegistry());
    private static readonly SchemaValidator _validator = new();

    protected readonly IHttpClientFactory HttpClientFactory;
    protected readonly ILogger Logger;
    public string MyceliumUrl { get; }
    private readonly string? _serviceToken;

    public Guid HandlerId { get; } = Guid.NewGuid();

    /// <summary>
    /// Failure policy for outbound contract violations. Debug builds throw to surface
    /// schema drift immediately during development; Release builds log and let the
    /// request through so a stale schema never blocks production traffic. Test
    /// subclasses override this property to pin both paths deterministically.
    /// </summary>
    protected virtual SchemaViolationMode OutboundViolationMode =>
#if DEBUG
        SchemaViolationMode.Throw;
#else
        SchemaViolationMode.Log;
#endif

    /// <summary>
    /// Validates <paramref name="json"/> against the schema with <paramref name="schemaId"/>
    /// according to the current <see cref="OutboundViolationMode"/>. Used by RegisterAsync,
    /// GetTokenAsync, and (Phase 4) service-specific subclass calls. Name is a slight
    /// misnomer for inbound traffic (SignalR events, mycelium responses) but the validation
    /// shape is direction-agnostic; treat "outbound" as "crossing the MyceliumClient boundary".
    /// </summary>
    protected void ValidateOutbound(string json, string schemaId)
    {
        var schema = _registry.Value.Get(schemaId);
        switch (OutboundViolationMode)
        {
            case SchemaViolationMode.Throw:
                _validator.ValidateOrThrow(json, schema, schemaId);
                break;
            case SchemaViolationMode.Log:
                _validator.ValidateForLog(json, schema, schemaId, Logger);
                break;
        }
    }

    protected MyceliumClientBase(IHttpClientFactory httpClientFactory, ILogger logger, string myceliumUrl, string? serviceToken = null)
    {
        HttpClientFactory = httpClientFactory;
        Logger = logger;
        MyceliumUrl = myceliumUrl;
        _serviceToken = serviceToken;
    }

    /// <summary>
    /// Gets a JWT token for authenticating with Mycelium.
    /// Uses the service token passed via --token if available, otherwise falls back
    /// to the legacy open token endpoint for backward compatibility.
    /// </summary>
    public async Task<string?> GetTokenAsync()
    {
        if (!string.IsNullOrEmpty(_serviceToken))
            return _serviceToken;

        string body;
        try
        {
            var client = HttpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var response = await client.PostAsync($"{MyceliumUrl}/api/auth/token", null);
            response.EnsureSuccessStatusCode();

            body = await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to get token from mycelium");
            return null;
        }

        // Validation runs outside the network try/catch so contract violations are not
        // swallowed: Throw mode propagates ContractValidationException to the caller;
        // Log mode warns and falls through to best-effort parse.
        ValidateOutbound(body, TokenResponseSchemaId);

        try
        {
            var result = JsonSerializer.Deserialize<JsonElement>(body);
            return result.GetProperty("token").GetString();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to parse token from mycelium response");
            return null;
        }
    }

    /// <summary>Creates an HttpClient with Bearer auth header set from current token.</summary>
    protected async Task<HttpClient> CreateAuthenticatedClientAsync(TimeSpan? timeout = null)
    {
        var token = await GetTokenAsync()
            ?? throw new InvalidOperationException("Failed to get authentication token");

        var client = HttpClientFactory.CreateClient();
        client.Timeout = timeout ?? TimeSpan.FromSeconds(5);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Registers this service with Mycelium.</summary>
    public async Task<bool> RegisterAsync(int port, string serviceName, string startCommand)
    {
        var registration = new
        {
            handlerId = HandlerId,
            serviceName,
            endpointUrl = $"http://localhost:{port}",
            startCommand,
            stopEndpoint = $"http://localhost:{port}/shutdown",
            healthEndpoint = $"http://localhost:{port}/health"
        };

        var json = JsonSerializer.Serialize(registration);

        // Validate the outbound payload before any network call. Throw mode fails fast in
        // dev; Log mode warns and lets the request through so production never blocks on
        // stale schemas. Schema-violation exceptions intentionally propagate past the
        // network try/catch below.
        ValidateOutbound(json, MyceliumRegisterRequestSchemaId);

        try
        {
            var client = await CreateAuthenticatedClientAsync();
            var response = await client.PostAsync(
                $"{MyceliumUrl}/api/mycelium/register",
                new StringContent(json, Encoding.UTF8, "application/json"));

            if (response.IsSuccessStatusCode)
            {
                Logger.LogInformation("Registered with mycelium as {HandlerId}", HandlerId);
                return true;
            }

            Logger.LogError("Registration failed: {StatusCode}", response.StatusCode);
            return false;
        }
        catch (InvalidOperationException)
        {
            Logger.LogError("Cannot register: failed to get token");
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error registering with mycelium");
            return false;
        }
    }

    /// <summary>Deregisters this service from Mycelium.</summary>
    public virtual async Task DeregisterAsync()
    {
        try
        {
            var token = await GetTokenAsync();
            if (token == null)
            {
                Logger.LogWarning("Cannot deregister: failed to get token");
                return;
            }

            var client = HttpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.DeleteAsync($"{MyceliumUrl}/api/mycelium/services/{HandlerId}");

            if (response.IsSuccessStatusCode)
                Logger.LogInformation("Deregistered from mycelium");
            else
                Logger.LogWarning("Deregistration returned: {StatusCode}", response.StatusCode);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deregistering from mycelium");
        }
    }
}
