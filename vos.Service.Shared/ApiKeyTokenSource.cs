using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace vos.Service.Shared;

/// <summary>
/// The bearer a service holds when it was given an API key instead of a token.
///
/// A key does not expire, but the token it is exchanged for lasts minutes — so the token is held and
/// the exchange repeated shortly before it runs out, rather than on every call, which would double
/// the traffic of every write. A token stating no expiry is never held: it would be trusted forever
/// and fail only once something depended on it.
///
/// An exchange that produced no usable token is remembered for a few seconds. One attempt runs at a
/// time, so without that memory every caller waiting on a broker that is refusing or unreachable
/// would take its turn at the full exchange timeout, and the last of them would wait for all the
/// others first.
/// </summary>
public sealed class ApiKeyTokenSource
{
    public const string Header = "X-API-Key";

    private static readonly TimeSpan ReplacementLeadTime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ExchangeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelayAfterAFruitlessExchange = TimeSpan.FromSeconds(5);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;
    private readonly string _myceliumUrl;
    private readonly string _apiKey;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _exchange = new(1, 1);

    private sealed record HeldToken(string Token, DateTimeOffset ExpiresAt);

    private HeldToken? _held;
    private DateTimeOffset _earliestNextExchange = DateTimeOffset.MinValue;

    public ApiKeyTokenSource(
        IHttpClientFactory httpClientFactory, ILogger logger, string myceliumUrl, string apiKey,
        Func<DateTimeOffset>? now = null)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _myceliumUrl = myceliumUrl;
        _apiKey = apiKey;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (Usable(_held) is { } held)
            return held.Token;

        await _exchange.WaitAsync(cancellationToken);
        try
        {
            if (Usable(_held) is { } refreshedMeanwhile)
                return refreshedMeanwhile.Token;

            if (_now() < _earliestNextExchange)
                return null;

            var minted = await MintAsync(cancellationToken);
            if (minted is null)
            {
                _earliestNextExchange = _now() + RetryDelayAfterAFruitlessExchange;
                return null;
            }

            _held = minted;
            return minted.Token;
        }
        finally
        {
            _exchange.Release();
        }
    }

    private HeldToken? Usable(HeldToken? held) =>
        held is not null && held.ExpiresAt - _now() > ReplacementLeadTime ? held : null;

    private async Task<HeldToken?> MintAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_myceliumUrl}/api/auth/token");
            request.Headers.Add(Header, _apiKey);

            // A timeout per request, not on the client: a shared HttpClient refuses a Timeout change
            // once it has sent anything.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ExchangeTimeout);

            var response = await client.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Mycelium refused to exchange the API key ({Status})", (int)response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            var issued = JsonSerializer.Deserialize<JsonElement>(body);
            var token = issued.TryGetProperty("token", out var value) ? value.GetString() : null;

            var payload = JwtPayload.Read(token);
            if (payload is null || JwtPayload.ExpiryOf(payload.Value) is not { } expiresAt)
            {
                _logger.LogWarning("Mycelium exchanged the API key for a token this service cannot hold");
                return null;
            }

            return new HeldToken(token!, expiresAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not exchange the API key for a token");
            return null;
        }
    }
}
