using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using vos.Service.Shared;

namespace vos.Service.Forage.Services;

// Fetches one source by asking Mycelium to forward to the endpoint service that performs fetches.
// Mycelium proxies the body through to that service's /handle, so the reshape and the write onto the
// Site both happen there — Forage decides which sources and with what values, never how a fetch
// is made.
//
// The same route serves the calls a run makes to work something out rather than to record a reading: a
// registration carrying no reshape expression is answered with the provider's own body and writes
// nothing, which is what the division lookups need. One class, because the two differ only in what they
// do with the answer — reaching the fetching service, the timeout and the credential are the same.
public sealed class EndpointServiceSourceFetcher : MyceliumClientBase, ISourceFetcher, IEndpointBodyReader
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
            var response = await CallAsync(new { endpointName, addressParameters, subjectId }, bounded.Token);

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

    // The provider's own body, for a call a run makes to work something out. No subject is named because
    // nothing is written: the registration carries no reshape expression, so the fetching service returns
    // what the provider said and ingests none of it.
    public async Task<string?> ReadAsync(
        string endpointName,
        IReadOnlyDictionary<string, string> addressParameters,
        CancellationToken cancellationToken)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(_sourceTimeout);

        try
        {
            var response = await CallAsync(new { endpointName, addressParameters }, bounded.Token);
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
                endpointName, _fetcherSubdomain, _sourceTimeout.TotalSeconds);
            return null;
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Reading {Endpoint} through {Subdomain} failed",
                endpointName, _fetcherSubdomain);
            return null;
        }
    }

    private async Task<HttpResponseMessage> CallAsync(object request, CancellationToken cancellationToken)
    {
        var client = await CreateAuthenticatedClientAsync(_sourceTimeout);
        return await client.PostAsJsonAsync(
            $"{MyceliumUrl}/api/endpoints/{Uri.EscapeDataString(_fetcherSubdomain)}", request, cancellationToken);
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
