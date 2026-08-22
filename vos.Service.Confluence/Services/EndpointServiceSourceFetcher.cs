using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using vos.Service.Shared;

namespace vos.Service.Confluence.Services;

// Fetches one source by asking Mycelium to forward to the endpoint service that performs fetches.
// Mycelium proxies the body through to that service's /handle, so the reshape and the write onto the
// Site both happen there — Confluence decides which sources and with what values, never how a fetch
// is made.
public sealed class EndpointServiceSourceFetcher : MyceliumClientBase, ISourceFetcher
{
    private readonly string _fetcherSubdomain;
    private readonly TimeSpan _sourceTimeout;

    public EndpointServiceSourceFetcher(
        IHttpClientFactory httpClientFactory,
        ILogger<EndpointServiceSourceFetcher> logger,
        string myceliumUrl,
        string? serviceToken,
        string fetcherSubdomain,
        TimeSpan sourceTimeout)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken)
    {
        _fetcherSubdomain = fetcherSubdomain;
        _sourceTimeout = sourceTimeout;
    }

    public async Task<SourceOutcome> FetchAsync(
        Guid siteId,
        string sourceName,
        string endpointName,
        IReadOnlyDictionary<string, string> addressParameters,
        CancellationToken cancellationToken)
    {
        // The timeout is the run's own, not the fetching service's: a provider that accepts the
        // connection and then goes quiet is the failure most likely to be met in production, and a
        // run that waited on it indefinitely would stall for every other source behind the bound.
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(_sourceTimeout);

        try
        {
            var client = await CreateAuthenticatedClientAsync(_sourceTimeout);
            var response = await client.PostAsJsonAsync(
                $"{MyceliumUrl}/api/endpoints/{Uri.EscapeDataString(_fetcherSubdomain)}",
                new { endpointName, addressParameters, subjectId = siteId },
                bounded.Token);

            if (response.IsSuccessStatusCode)
                return new SourceOutcome(sourceName, true, null);

            // The upstream body can carry a provider's own message; it is the most useful thing a
            // planner can be told about why a value is missing, so it is reported rather than logged
            // and replaced with a generic line.
            var body = await response.Content.ReadAsStringAsync(bounded.Token);
            return new SourceOutcome(sourceName, false, $"{(int)response.StatusCode}: {Summarize(body)}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SourceOutcome(
                sourceName, false, $"No answer within {_sourceTimeout.TotalSeconds:0} seconds.");
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Fetching {Source} through {Subdomain} failed", sourceName, _fetcherSubdomain);
            return new SourceOutcome(sourceName, false, exception.Message);
        }
    }

    // A reason is read by a person deciding whether a gap matters, so it carries the provider's own
    // words but not a whole error document.
    private const int LongestReason = 300;

    private static string Summarize(string body)
    {
        var trimmed = body.Trim();
        return trimmed.Length <= LongestReason ? trimmed : trimmed[..LongestReason] + "…";
    }
}
