using Microsoft.Extensions.Logging;

namespace vos.Service.Shared;

/// <summary>
/// The credential a service presents to the broker: the bearer the work in hand arrived with, else the
/// one the service was launched with.
///
/// A service that reaches the broker without deriving from <see cref="MyceliumClientBase"/> asks this,
/// so which credential a service presents has one answer however the call is made — and a service given
/// a key rather than a token is not left calling with nothing.
/// </summary>
public sealed class ServiceCredential
{
    private readonly string? _serviceToken;
    private readonly ApiKeyTokenSource? _apiKeyTokens;

    public ServiceCredential(
        IHttpClientFactory httpClientFactory, ILogger logger, string myceliumUrl,
        string? serviceToken = null, string? apiKey = null)
    {
        _serviceToken = serviceToken;
        _apiKeyTokens = string.IsNullOrEmpty(apiKey)
            ? null
            : new ApiKeyTokenSource(httpClientFactory, logger, myceliumUrl, apiKey);
    }

    /// <summary>Whether the service has anything of its own to present. False only when it holds no
    /// credential and no work is in hand, which is when a caller has to obtain one some other way.</summary>
    public bool Holds =>
        !string.IsNullOrEmpty(MyceliumModelToken.Current)
        || _apiKeyTokens != null
        || !string.IsNullOrEmpty(_serviceToken);

    /// <summary>Null when the service holds nothing, and when a key it holds could not be exchanged: a
    /// key that failed answers nothing rather than falling back to a credential reaching further.</summary>
    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(MyceliumModelToken.Current))
            return MyceliumModelToken.Current;

        if (_apiKeyTokens != null)
            return await _apiKeyTokens.GetTokenAsync(cancellationToken);

        return string.IsNullOrEmpty(_serviceToken) ? null : _serviceToken;
    }
}
