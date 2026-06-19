using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using vos.ManagedMicroservice.Shared.Contracts.Validation;

namespace vos.ManagedMicroservice.Shared;

public abstract class MyceliumClientBase
{
    private const string MyceliumRegisterRequestSchemaId = "https://villageos/contracts/mycelium-register-request.schema.json";
    private const string TokenResponseSchemaId = "https://villageos/contracts/token-response.schema.json";

    private static readonly Lazy<SchemaRegistry> _registry = new(() => new SchemaRegistry());
    private static readonly SchemaValidator _validator = new();

    protected readonly IHttpClientFactory HttpClientFactory;
    protected readonly ILogger Logger;
    public string MyceliumUrl { get; }
    private readonly string? _serviceToken;

    public Guid HandlerId { get; } = Guid.NewGuid();

    /// <summary>
    /// Failure policy for outbound contract violations: Debug throws to surface schema drift;
    /// Release logs so a stale schema never blocks production traffic.
    /// </summary>
    protected virtual SchemaViolationMode OutboundViolationMode =>
#if DEBUG
        SchemaViolationMode.Throw;
#else
        SchemaViolationMode.Log;
#endif

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

        // Outside the network try/catch so contract violations are not swallowed.
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

    protected async Task<HttpClient> CreateAuthenticatedClientAsync(TimeSpan? timeout = null)
    {
        var token = await GetTokenAsync()
            ?? throw new InvalidOperationException("Failed to get authentication token");

        var client = HttpClientFactory.CreateClient();
        client.Timeout = timeout ?? TimeSpan.FromSeconds(5);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

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

        // Outside the network try/catch so schema-violation exceptions are not swallowed.
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
