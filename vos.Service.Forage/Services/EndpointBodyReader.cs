using vos.Service.Shared;

namespace vos.Service.Forage.Services;

public sealed class EndpointBodyReader : MyceliumClientBase, IEndpointBodyReader
{
    private readonly string _fetcherSubdomain;
    private readonly TimeSpan _timeout;

    public EndpointBodyReader(
        IHttpClientFactory httpClientFactory,
        ILogger<EndpointBodyReader> logger,
        string myceliumUrl,
        LaunchToken? launchToken,
        string fetcherSubdomain,
        TimeSpan timeout,
        string? apiKey = null)
        : base(httpClientFactory, logger, myceliumUrl, launchToken, apiKey: apiKey)
    {
        _fetcherSubdomain = fetcherSubdomain;
        _timeout = timeout;
    }

    // No subject is named because nothing is written: the registration carries no reshape expression, so
    // the fetching service has no reading to put anywhere.
    public async Task<string?> ReadAsync(
        string endpointName,
        IReadOnlyDictionary<string, string> addressParameters,
        CancellationToken cancellationToken)
    {
        // Mycelium's limit on the passed-on request ends a call to a provider that goes quiet. This bound is
        // the backstop for a broker that does not answer at all.
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(_timeout);

        try
        {
            var client = await CreateAuthenticatedClientAsync(_timeout);
            var response = await client.PostAsJsonAsync(
                $"{MyceliumUrl}/api/endpoints/{Uri.EscapeDataString(_fetcherSubdomain)}",
                new { endpointName, addressParameters }, bounded.Token);
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadAsStringAsync(bounded.Token);

            // The provider's own words, because a caller deciding whether to ask a coarser question needs
            // to know it was refused rather than answered nothing.
            Logger.LogWarning("Reading {Endpoint} through {Subdomain} was answered {Status}: {Body}",
                endpointName, _fetcherSubdomain, (int)response.StatusCode,
                Summarize(await response.Content.ReadAsStringAsync(bounded.Token)));
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.LogWarning("Reading {Endpoint} through {Subdomain} was not answered within {Seconds} seconds",
                endpointName, _fetcherSubdomain, _timeout.TotalSeconds);
            return null;
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Reading {Endpoint} through {Subdomain} failed",
                endpointName, _fetcherSubdomain);
            return null;
        }
    }

    // A refusal is read by a person, so the log carries the provider's own words but not a whole error document.
    private const int LongestReason = 300;

    private static string Summarize(string body)
    {
        var trimmed = body.Trim();
        return trimmed.Length <= LongestReason ? trimmed : trimmed[..LongestReason] + "…";
    }
}
