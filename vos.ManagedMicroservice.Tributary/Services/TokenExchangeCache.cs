using System.Collections.Concurrent;
using System.Text.Json;

namespace vos.ManagedMicroservice.Tributary.Services;

/// <summary>
/// The request that mints a token: where to POST, which form fields to send, and how to read the token
/// (and its expiry) back out of the response by simple dotted paths. It is source-agnostic — ArcGIS
/// <c>generateToken</c> and OAuth2 client-credentials are the same shape with different field names and
/// <see cref="ExpiryUnit"/>, supplied entirely by the endpoint template.
/// </summary>
public sealed record TokenExchangeRequest(
    string TokenUrl,
    IReadOnlyDictionary<string, string> RequestFields,
    string TokenPath,
    string? ExpiryPath,
    string? ExpiryUnit);

/// <summary>
/// Per-process cache of bearer tokens obtained by a credential exchange (Task #5470). It POSTs the
/// configured <see cref="TokenExchangeRequest.RequestFields"/> to the token endpoint, extracts the
/// token (and optional expiry) by simple dotted path, and reuses the token until ~75% of its lifetime
/// elapses, then refreshes. Nothing here knows about ArcGIS — the field names and expiry unit are the
/// only source-specific bits, and they arrive as config.
///
/// Keyed by the token URL plus the full request-field set, so distinct credentials/services cache
/// independently. One fetch gate per key: same-key callers collapse onto a single mint (no stampede);
/// distinct keys mint concurrently. The clock is a <see cref="TimeProvider"/> for deterministic tests.
/// </summary>
public sealed class TokenExchangeCache
{
    /// <summary>Expiry is an absolute Unix time in milliseconds (e.g. ArcGIS <c>expires</c>).</summary>
    public const string ExpiryUnitEpochMillis = "epochMillis";
    /// <summary>Expiry is an absolute Unix time in seconds.</summary>
    public const string ExpiryUnitEpochSeconds = "epochSeconds";
    /// <summary>Expiry is a lifetime in seconds from now (e.g. OAuth2 <c>expires_in</c>).</summary>
    public const string ExpiryUnitSeconds = "seconds";

    private const double RefreshAtLifetimeFraction = 0.75;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TokenExchangeCache> _logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    public TokenExchangeCache(IHttpClientFactory httpClientFactory, TimeProvider timeProvider, ILogger<TokenExchangeCache> logger)
    {
        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Return a valid token for <paramref name="request"/>, minting a fresh one only when the cache is
    /// cold or the cached token has passed its refresh threshold. Throws
    /// <see cref="InvalidOperationException"/> when the token endpoint fails or returns no token.
    /// </summary>
    public async Task<string> GetTokenAsync(TokenExchangeRequest request, CancellationToken cancellationToken = default)
    {
        var key = BuildKey(request);

        // Fast path: a still-fresh cached token needs no lock.
        if (_cache.TryGetValue(key, out var cached) && _timeProvider.GetUtcNow() < cached.RefreshAt)
            return cached.Token;

        // Slow path: serialize per key and re-check, so a burst of same-key callers shares one mint.
        var gate = _gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(key, out var entry) && _timeProvider.GetUtcNow() < entry.RefreshAt)
                return entry.Token;

            var fresh = await RequestTokenAsync(request, cancellationToken);
            _cache[key] = fresh;
            return fresh.Token;
        }
        finally
        {
            gate.Release();
        }
    }

    private static string BuildKey(TokenExchangeRequest request)
    {
        var fields = string.Join("&", request.RequestFields
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value}"));
        return $"{request.TokenUrl}\n{fields}";
    }

    private async Task<CacheEntry> RequestTokenAsync(TokenExchangeRequest request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Requesting token from {TokenUrl}", request.TokenUrl);

        var client = _httpClientFactory.CreateClient();
        using var content = new FormUrlEncodedContent(request.RequestFields);
        using var response = await client.PostAsync(request.TokenUrl, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Token request to '{request.TokenUrl}' failed with status {(int)response.StatusCode}.");

        return ParseTokenResponse(request, body);
    }

    private CacheEntry ParseTokenResponse(TokenExchangeRequest request, string body)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(body);
            root = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Token response from '{request.TokenUrl}' was not valid JSON.", ex);
        }

        // A failure envelope (e.g. ArcGIS {error:{...}}) simply has no token at the configured path.
        if (!TryNavigate(root, request.TokenPath, out var tokenElement) || tokenElement.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException(
                $"Token response from '{request.TokenUrl}' had no token at path '{request.TokenPath}'.");

        return new CacheEntry(tokenElement.GetString()!, ComputeRefreshAt(request, root));
    }

    private DateTimeOffset ComputeRefreshAt(TokenExchangeRequest request, JsonElement root)
    {
        var now = _timeProvider.GetUtcNow();

        // Without a usable expiry, treat the token as immediately stale so the next call refetches
        // rather than caching it forever (same-key callers are still deduped).
        if (string.IsNullOrEmpty(request.ExpiryPath)
            || !TryNavigate(root, request.ExpiryPath, out var expiryElement)
            || expiryElement.ValueKind != JsonValueKind.Number
            || !expiryElement.TryGetInt64(out var expiryValue))
        {
            return now;
        }

        var expiresAt = request.ExpiryUnit switch
        {
            ExpiryUnitEpochSeconds => DateTimeOffset.FromUnixTimeSeconds(expiryValue),
            ExpiryUnitSeconds => now + TimeSpan.FromSeconds(expiryValue),
            _ => DateTimeOffset.FromUnixTimeMilliseconds(expiryValue),
        };

        var lifetime = expiresAt - now;
        return lifetime <= TimeSpan.Zero ? now : now + lifetime * RefreshAtLifetimeFraction;
    }

    /// <summary>Walk a dotted path (e.g. <c>data.token</c>) through nested JSON objects.</summary>
    private static bool TryNavigate(JsonElement root, string dottedPath, out JsonElement result)
    {
        result = root;
        foreach (var segment in dottedPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty(segment, out var next))
            {
                result = default;
                return false;
            }
            result = next;
        }
        return true;
    }

    private readonly record struct CacheEntry(string Token, DateTimeOffset RefreshAt);
}
