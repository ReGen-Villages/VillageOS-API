using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace vos.Microservice.Shared;

/// <summary>
/// Base class for microservice BrokerClients that communicate with the VOS Broker.
/// Provides shared token management, registration, deregistration, and authenticated HTTP helpers.
/// Used by IsHandler, Metabolism, and IntegrationRegistry handler services.
/// </summary>
public abstract class BrokerClientBase
{
    protected readonly IHttpClientFactory HttpClientFactory;
    protected readonly ILogger Logger;
    public string BrokerUrl { get; }
    private readonly string? _serviceToken;

    public Guid HandlerId { get; } = Guid.NewGuid();

    protected BrokerClientBase(IHttpClientFactory httpClientFactory, ILogger logger, string brokerUrl, string? serviceToken = null)
    {
        HttpClientFactory = httpClientFactory;
        Logger = logger;
        BrokerUrl = brokerUrl;
        _serviceToken = serviceToken;
    }

    /// <summary>
    /// Gets a JWT token for authenticating with the broker.
    /// Uses the service token passed via --token if available, otherwise falls back
    /// to the legacy open token endpoint for backward compatibility.
    /// </summary>
    public async Task<string?> GetTokenAsync()
    {
        if (!string.IsNullOrEmpty(_serviceToken))
            return _serviceToken;

        try
        {
            var client = HttpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var response = await client.PostAsync($"{BrokerUrl}/api/auth/token", null);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            return result.GetProperty("token").GetString();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to get token from broker");
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

    /// <summary>Registers this service with the broker.</summary>
    public async Task<bool> RegisterAsync(int port, string serviceName, string startCommand)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync();

            var registration = new
            {
                handlerId = HandlerId,
                serviceName,
                endpointUrl = $"http://localhost:{port}",
                startCommand,
                stopEndpoint = $"http://localhost:{port}/shutdown",
                healthEndpoint = $"http://localhost:{port}/health"
            };

            var response = await client.PostAsJsonAsync($"{BrokerUrl}/api/broker/register", registration);

            if (response.IsSuccessStatusCode)
            {
                Logger.LogInformation("Registered with broker as {HandlerId}", HandlerId);
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
            Logger.LogError(ex, "Error registering with broker");
            return false;
        }
    }

    /// <summary>Deregisters this service from the broker.</summary>
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

            var response = await client.DeleteAsync($"{BrokerUrl}/api/broker/services/{HandlerId}");

            if (response.IsSuccessStatusCode)
                Logger.LogInformation("Deregistered from broker");
            else
                Logger.LogWarning("Deregistration returned: {StatusCode}", response.StatusCode);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deregistering from broker");
        }
    }
}
