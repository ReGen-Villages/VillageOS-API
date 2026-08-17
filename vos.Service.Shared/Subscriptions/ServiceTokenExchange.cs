using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace vos.Service.Shared.Subscriptions;

/// <summary>Trades a bearer a daemon already holds for one that outlives the request it arrived on.</summary>
public interface IServiceTokenExchange
{
    /// <summary>The longer-lived bearer for the same model, or null when Mycelium would not issue one.</summary>
    Task<ModelScopedBearer?> ExchangeAsync(string bearer, CancellationToken cancellationToken = default);
}

/// <summary>
/// The bearer forwarded with a /handle call names the caller's model but expires in minutes, and a change
/// subscription has to outlast that. Mycelium mints a full-lifetime replacement for whoever already holds a
/// valid one, reading the model from the token rather than the request — so this asks for nothing and can
/// reach no project the caller could not already.
/// </summary>
public sealed class ServiceTokenExchange : IServiceTokenExchange
{
    private const string Route = "/api/auth/service-token";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;
    private readonly string _myceliumUrl;

    public ServiceTokenExchange(IHttpClientFactory httpClientFactory, ILogger logger, string myceliumUrl)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _myceliumUrl = myceliumUrl;
    }

    public async Task<ModelScopedBearer?> ExchangeAsync(string bearer, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

            var response = await client.PostAsync($"{_myceliumUrl}{Route}", null, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Mycelium refused to extend a service token ({Status})", (int)response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var issued = JsonSerializer.Deserialize<JsonElement>(body);
            var extended = ModelScopedBearer.Read(
                issued.TryGetProperty("token", out var token) ? token.GetString() : null);

            if (extended is null)
                _logger.LogWarning("Mycelium returned a service token this service cannot read");

            return extended;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not extend a service token");
            return null;
        }
    }
}
