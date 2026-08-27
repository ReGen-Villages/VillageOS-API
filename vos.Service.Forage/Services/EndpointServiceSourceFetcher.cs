using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using vos.Service.Shared;

namespace vos.Service.Forage.Services;

// Fetches one source by asking Mycelium to forward to the endpoint service that performs fetches.
// Mycelium proxies the body through to that service's /handle, so the reshape and the write onto the
// Site both happen there — Forage decides which sources and with what values, never how a fetch
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
        TimeSpan sourceTimeout,
        string? apiKey = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken, apiKey: apiKey)
    {
        _fetcherSubdomain = fetcherSubdomain;
        _sourceTimeout = sourceTimeout;
    }

    public async Task<SourceOutcome> FetchAsync(
        Guid subjectId,
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
                new { endpointName, addressParameters, subjectId },
                bounded.Token);

            if (response.IsSuccessStatusCode)
                return new SourceOutcome(sourceName, true, null,
                    SubjectId: subjectId,
                    Written: await WrittenIn(response, bounded.Token));

            // The upstream body can carry a provider's own message; it is the most useful thing a
            // planner can be told about why a value is missing, so it is reported rather than logged
            // and replaced with a generic line.
            var body = await response.Content.ReadAsStringAsync(bounded.Token);
            return new SourceOutcome(sourceName, false, $"{(int)response.StatusCode}: {Summarize(body)}",
                SubjectId: subjectId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SourceOutcome(
                sourceName, false, $"No answer within {_sourceTimeout.TotalSeconds:0} seconds.",
                SubjectId: subjectId);
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Fetching {Source} through {Subdomain} failed", sourceName, _fetcherSubdomain);
            return new SourceOutcome(sourceName, false, exception.Message, SubjectId: subjectId);
        }
    }

    // The values the call wrote, from the fetching service's own report of them. Only text can name a
    // vocabulary member, so only text is kept. Null where the body carries no report — an ingest that
    // went through the bulk path, or a body this cannot read — which resolves nothing rather than
    // failing a fetch that already succeeded.
    private static async Task<IReadOnlyDictionary<string, string>?> WrittenIn(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (body.RootElement.ValueKind != JsonValueKind.Object
                || !body.RootElement.TryGetProperty("written", out var written)
                || written.ValueKind != JsonValueKind.Object)
                return null;

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in written.EnumerateObject())
                if (entry.Value.ValueKind == JsonValueKind.String)
                    values[entry.Name] = entry.Value.GetString() ?? string.Empty;
            return values;
        }
        catch (JsonException)
        {
            return null;
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
