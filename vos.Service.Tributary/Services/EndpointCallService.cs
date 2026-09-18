using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using vos.Service.Tributary.Helpers;
using vos.Service.Tributary.Models;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Tributary.Services;

// Resolves an endpoint Thing's effective properties and performs the outbound HTTP call — auth-kinds,
// offset paging, binary response envelopes, and the optional JSONata reshape whose output is ingested
// as observations. Returns a framework-free EndpointCallResult; callers map it to their own response
// type. The checks themselves live in EndpointCallChecks; this class reads the model, runs them in
// order, and makes the call.
public sealed class EndpointCallService
{
    private readonly MyceliumClient _mycelium;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ObservationIngestService _observationService;
    private readonly TokenExchangeCache _tokenExchangeCache;
    private readonly ILogger<EndpointCallService> _logger;

    private readonly ISubscriptionClient _subscriptions;

    private readonly DiskResponseCache _diskCache;

    public EndpointCallService(
        MyceliumClient mycelium,
        IHttpClientFactory httpClientFactory,
        ObservationIngestService observationService,
        TokenExchangeCache tokenExchangeCache,
        ISubscriptionClient subscriptions,
        DiskResponseCache diskCache,
        ILogger<EndpointCallService> logger)
    {
        _mycelium = mycelium;
        _httpClientFactory = httpClientFactory;
        _observationService = observationService;
        _tokenExchangeCache = tokenExchangeCache;
        _subscriptions = subscriptions;
        _diskCache = diskCache;
        _logger = logger;
    }

    // One scoped read answers every role, and the `observed` edges the endpoint already carries with
    // them — those edges are in the same snapshot whether or not anything reads them, so provenance
    // costs no second call. Unsubscribing in a finally keeps a failed resolution from leaving a live
    // subscription on the gateway for the rest of the process's life.
    //
    // Null means the read failed, never that the endpoint reaches no kind — the two must not collapse,
    // because an unreachable gateway would otherwise read as an endpoint needing no credential and the
    // call would go out unauthenticated. The subscription client throws on any non-success status, so
    // the catch is what keeps a momentary gateway failure from escaping this method as an exception
    // rather than the 502 every other gateway call here produces.
    private async Task<(IReadOnlyDictionary<string, ResolvedKind> Kinds, ObservedEdges Observed)?> ReadEndpointAsync(
        Guid endpointId, CancellationToken cancellationToken)
    {
        SubscribeResult subscribed;
        try
        {
            subscribed = await _subscriptions.SubscribeAsync(
                EndpointKindResolver.SelectorFor(endpointId), cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to read which kinds endpoint {EndpointId} reaches", endpointId);
            return null;
        }

        try
        {
            return (EndpointKindResolver.Resolve(subscribed.Snapshot, endpointId),
                    ObservedEdges.Resolve(subscribed.Snapshot, endpointId));
        }
        finally
        {
            try
            {
                await _subscriptions.UnsubscribeAsync(subscribed.SubscriptionId, cancellationToken);
            }
            catch (Exception exception)
            {
                // The read already succeeded; failing to release the subscription must not lose it.
                _logger.LogWarning(exception, "Failed to release the kind-resolution subscription {SubscriptionId}",
                    subscribed.SubscriptionId);
            }
        }
    }

    public async Task<EndpointCallResult> ExecuteAsync(EndpointCallRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.EndpointName))
            return Refusal.BadRequest("Request must include a non-empty endpointName.").ToResult();

        var thing = await _mycelium.FindThingByNameAsync(request.EndpointName);
        if (thing == null)
            return new Refusal(404, $"Endpoint thing not found: {request.EndpointName}").ToResult();

        var effective = await _mycelium.GetEffectivePropertiesAsync(thing.Value.Id);
        if (effective == null)
            return Problem(500, "Endpoint resolution failed", "Failed to resolve effective properties for endpoint thing.");

        if (await ReadEndpointAsync(thing.Value.Id, cancellationToken) is not { } endpoint)
            return Problem(502, "Endpoint resolution failed",
                "Failed to read which kinds the endpoint reaches. Refusing rather than calling out as though it reaches none.");

        // ---- The checks, in the order they are relied on ----
        // What a kind requires is checked before any mechanism reads its settings, so an endpoint
        // that under-supplies is refused in the kind's words rather than the mechanism's. The body
        // kind comes before the reshape, which refuses a transform over bytes. Each clash check comes
        // after the last thing it combines, so an unimplemented kind is refused as such and never as
        // a clash with something it is not. Everything else is in the order the properties are read.
        var kinds = EndpointKinds.Reached(endpoint.Kinds);

        if (EndpointCallChecks.UnmetRequirement(kinds, effective) is { } unmet)
            return unmet.ToResult();
        if (!EndpointCallChecks.TryBodyKind(kinds.Body, out var binaryResponse, out var refusal))
            return refusal.ToResult();
        if (!EndpointCallChecks.TryAddress(effective, request.AddressParameters, out var address, out refusal))
            return refusal.ToResult();
        if (!EndpointCallChecks.TryReshape(effective, request.ResponseTransform, binaryResponse, out var reshape, out refusal))
            return refusal.ToResult();
        if (!string.IsNullOrWhiteSpace(reshape.Registered))
            _logger.LogInformation("Endpoint {EndpointName} responseTransform: {Transform}", request.EndpointName, reshape.Registered);
        if (!EndpointCallChecks.TryOptionalMap(effective, "headers", out var headers, out refusal))
            return refusal.ToResult();
        if (!EndpointCallChecks.TryOptionalMap(effective, "queryParams", out var queryParams, out refusal))
            return refusal.ToResult();
        if (!EndpointCallChecks.TryRequestContentType(effective, out var requestContentType, out refusal))
            return refusal.ToResult();
        // Optional Accept for content-negotiating upstreams (e.g. image/tiff from an ImageServer);
        // a dedicated structural key like requestContentType, not an entry in the headers map.
        if (!EndpointCallChecks.TryOptionalText(effective, "acceptHeader", out var acceptHeader, out refusal))
            return refusal.ToResult();
        if (!EndpointCallChecks.TryTimeout(effective, out var timeout, out refusal))
            return refusal.ToResult();
        if (!EndpointCallChecks.TryCredential(kinds.Authentication, effective, out var credentialPlan, out refusal))
            return refusal.ToResult();
        if (!EndpointCallChecks.TryPaging(kinds.Paging, effective, out var pageConfig, out refusal))
            return refusal.ToResult();
        if (EndpointCallChecks.PagingClash(kinds, binaryResponse, pageConfig) is { } pagingClash)
            return pagingClash.ToResult();
        if (!EndpointCallChecks.TryCaching(kinds.Caching, effective, out var cacheFor, out refusal))
            return refusal.ToResult();
        if (EndpointCallChecks.CachingClash(kinds, cacheFor, pageConfig, address.Method, request.Body) is { } cachingClash)
            return cachingClash.ToResult();

        var endpointUri = address.Uri;
        var normalizedMethod = address.Method;

        try
        {
            // Mint the token now (deferred network call). Its own catch returns a generic 502 — the
            // upstream message can name the credential and must not leak; the real one is logged.
            var credential = credentialPlan?.PreMinted;
            if (credentialPlan?.Fetch is { } tokenFetch)
            {
                try
                {
                    credential = await _tokenExchangeCache.GetTokenAsync(tokenFetch);
                }
                catch (Exception tokenEx)
                {
                    _logger.LogError(tokenEx, "Token acquisition failed for {EndpointName}", request.EndpointName);
                    return Problem(502, "Token acquisition failed", "Failed to obtain an authentication token for the endpoint.");
                }
            }

            var effectiveQueryParams = queryParams != null
                ? new Dictionary<string, string>(queryParams, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var effectiveHeaders = headers != null
                ? new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(credential))
            {
                if (!string.IsNullOrEmpty(credentialPlan!.Header))
                    effectiveHeaders[credentialPlan.Header] = string.IsNullOrEmpty(credentialPlan.Scheme)
                        ? credential
                        : $"{credentialPlan.Scheme} {credential}";
                else
                    effectiveQueryParams[credentialPlan.Param] = credential;
            }

            // The key is the fully-resolved address — placeholders filled, query attached — plus
            // the Accept header, since the same address can answer with different formats (#5913).
            string? cacheKey = null;
            CachedResponse? cached = null;
            if (cacheFor is { } timeToLive)
            {
                cacheKey = DiskResponseCache.CacheKey(
                    OutboundRequest.ApplyQueryParameters(endpointUri, effectiveQueryParams), acceptHeader);
                cached = _diskCache.TryRead(request.EndpointName!, cacheKey, timeToLive);
            }

            if (binaryResponse)
            {
                if (cached is { } fromDisk)
                    return EndpointCallResult.Body(BinaryEnvelope(fromDisk.Bytes, fromDisk.ContentType), "application/json");

                // Bytes must be read before any string decode: ReadAsStringAsync replaces non-UTF-8
                // sequences with U+FFFD, which is lossy and irreversible. The envelope stays
                // application/json so the result flows through existing proxying unchanged.
                var (binaryStatus, bytes, upstreamContentType) = await CallEndpointBinaryAsync(
                    _httpClientFactory, endpointUri, normalizedMethod, request.Body, effectiveHeaders, effectiveQueryParams, requestContentType, acceptHeader, timeout, cancellationToken);

                var servedContentType = string.IsNullOrWhiteSpace(upstreamContentType) ? "application/octet-stream" : upstreamContentType;
                if (cacheKey != null && binaryStatus is >= 200 and < 300)
                    _diskCache.Write(request.EndpointName!, cacheKey, bytes, servedContentType);

                if (ProviderRefused(binaryStatus))
                {
                    var refusalWords = Encoding.UTF8.GetString(bytes);
                    return EndpointCallResult.Body(refusalWords, servedContentType, binaryStatus);
                }

                return EndpointCallResult.Body(BinaryEnvelope(bytes, servedContentType), "application/json");
            }

            int status;
            string body;
            string? contentType;

            if (cached is { } cachedText)
            {
                status = 200;
                body = Encoding.UTF8.GetString(cachedText.Bytes);
                contentType = cachedText.ContentType;
            }
            else if (pageConfig != null)
            {
                // Walk every page and aggregate before the transform runs below.
                var paging = pageConfig!;
                body = await OffsetPaginator.FetchAllPagesAsync(
                    async (offset, ct) =>
                    {
                        var pageParams = new Dictionary<string, string>(effectiveQueryParams, StringComparer.OrdinalIgnoreCase)
                        {
                            [paging.OffsetParam] = offset.ToString(CultureInfo.InvariantCulture)
                        };
                        if (paging.PageSizeParam != null && paging.PageSize is > 0)
                            pageParams[paging.PageSizeParam] = paging.PageSize.Value.ToString(CultureInfo.InvariantCulture);

                        var (pageStatus, pageBody, pageContentType) = await CallEndpointAsync(
                            _httpClientFactory, endpointUri, normalizedMethod, request.Body, effectiveHeaders, pageParams, requestContentType, acceptHeader, timeout, ct);
                        if (pageStatus is < 200 or >= 300)
                            throw new PageNotAnsweredException(pageStatus, pageBody, pageContentType, offset);
                        return pageBody;
                    },
                    paging);
                status = 200;
                contentType = "application/json";
            }
            else
            {
                (status, body, contentType) = await CallEndpointAsync(
                    _httpClientFactory, endpointUri, normalizedMethod, request.Body, effectiveHeaders, effectiveQueryParams, requestContentType, acceptHeader, timeout, cancellationToken);

                if (cacheKey != null && status is >= 200 and < 300)
                    _diskCache.Write(request.EndpointName!, cacheKey, Encoding.UTF8.GetBytes(body), contentType ?? "application/json");
            }

            if (ProviderRefused(status))
                return EndpointCallResult.Body(body, contentType ?? "application/json", status);

            if (reshape.Transform is { } transform && status is >= 200 and < 300)
            {
                var ingestResult = await _observationService.CreateObservationsAsync(
                    thing.Value.Id, transform, body, request.SubjectId, endpoint.Observed);
                if (!ingestResult.Success)
                    return new Refusal(400, ingestResult.Error ?? "Observation ingest failed.",
                        new Dictionary<string, object?> { ["detail"] = ingestResult.Detail }).ToResult();

                return EndpointCallResult.Ingested(new IngestSummary(
                    thing.Value.Id, ingestResult.EntitiesTouched, ingestResult.ObservationsSubmitted,
                    ingestResult.Written));
            }

            return EndpointCallResult.Body(body, contentType ?? "application/json");
        }
        catch (PageNotAnsweredException unanswered)
        {
            // The offset is about this walk, not the provider, so it goes to the log, not the answer.
            _logger.LogWarning(
                "Endpoint {EndpointName} was answered {Status} at offset {Offset}; the pages before it are dropped",
                request.EndpointName, unanswered.StatusCode, unanswered.Offset);
            return EndpointCallResult.Body(
                unanswered.Body, unanswered.ContentType ?? "application/json", unanswered.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Endpoint call failed for {EndpointName}", request.EndpointName);
            return Problem(502, "Endpoint call failed", ex.Message);
        }
    }

    // Any page outside 2xx ends the walk, 404 included — which a single call takes for "holds
    // nothing about this subject" and a page cannot, because the aggregate can never be completed.
    private sealed class PageNotAnsweredException(int statusCode, string body, string? contentType, int offset)
        : Exception($"The provider answered {statusCode} at offset {offset}.")
    {
        public int StatusCode { get; } = statusCode;
        public string Body { get; } = body;
        public string? ContentType { get; } = contentType;
        public int Offset { get; } = offset;
    }

    // 404 is the provider answering that it holds nothing about this subject — a complete answer, so
    // the call counts as done; asking again would never get a different one.
    private static bool ProviderRefused(int status) => status is (< 200 or >= 300) and not 404;

    private static string BinaryEnvelope(byte[] bytes, string contentType) =>
        JsonSerializer.Serialize(new
        {
            contentType,
            dataBase64 = Convert.ToBase64String(bytes),
            byteLength = bytes.Length
        });

    private static EndpointCallResult Problem(int status, string title, string detail) =>
        EndpointCallResult.Failure(new ProblemError(status, title, detail));

    private static async Task<(int StatusCode, string Body, string? ContentType)> CallEndpointAsync(
        IHttpClientFactory httpClientFactory,
        Uri endpointUri,
        string method,
        JsonElement body,
        IReadOnlyDictionary<string, string>? headers,
        IReadOnlyDictionary<string, string>? queryParameters,
        string requestContentType,
        string? acceptHeader,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = timeout;

        using var request = OutboundRequest.Build(method, endpointUri, body, headers, queryParameters, requestContentType, acceptHeader);

        using var response = await client.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.ToString();
        return ((int)response.StatusCode, content, contentType);
    }

    private static async Task<(int StatusCode, byte[] Body, string? ContentType)> CallEndpointBinaryAsync(
        IHttpClientFactory httpClientFactory,
        Uri endpointUri,
        string method,
        JsonElement body,
        IReadOnlyDictionary<string, string>? headers,
        IReadOnlyDictionary<string, string>? queryParameters,
        string requestContentType,
        string? acceptHeader,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = timeout;

        using var request = OutboundRequest.Build(method, endpointUri, body, headers, queryParameters, requestContentType, acceptHeader);

        using var response = await client.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.ToString();
        return ((int)response.StatusCode, content, contentType);
    }
}
