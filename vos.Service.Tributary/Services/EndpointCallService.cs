using System.Globalization;
using System.Text;
using System.Text.Json;
using vos.Service.Shared;
using Microsoft.Extensions.Logging;
using vos.Service.Tributary.Helpers;
using vos.Service.Tributary.Models;
using vos.Service.Shared.Subscriptions;
using vos.Service.Shared.Validation;

namespace vos.Service.Tributary.Services;

// Resolves an endpoint Thing's effective properties and performs the outbound HTTP call — auth-kinds,
// offset paging, binary response envelopes, and the optional JSONata reshape whose output is ingested
// as observations. Returns a framework-free EndpointCallResult; callers map it to their own response
// type.
public sealed class EndpointCallService
{
    private readonly MyceliumClient _mycelium;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ObservationIngestService _observationService;
    private readonly TokenExchangeCache _tokenExchangeCache;
    private readonly ILogger<EndpointCallService> _logger;

    private readonly ISubscriptionClient _subscriptions;

    // The mechanisms this service implements, by the name of the kind Thing each answers to. The
    // model owns which endpoints use which kind and what each requires; code owns only how the work
    // is done. A kind the model names and nothing here implements is refused saying both halves.
    private const string TokenExchangeMechanism = "TokenExchangeAuth";
    private const string OffsetPagingMechanism = "OffsetPaging";
    private const string BinaryBodyMechanism = "BinaryResponse";
    private const string JsonBodyMechanism = "JsonResponse";
    private const string DiskCacheMechanism = "DiskCache";

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

    private const string BinaryTransformClash =
        "A binary body cannot be combined with a response transform: there is no text to transform.";

    // Says what the model asked for and what this service can actually do, because either half alone
    // sends the reader to the wrong place.
    private static EndpointCallResult UnimplementedKind(string role, string named, params string[] implemented)
    {
        var error = $"The endpoint's '{role}' kind is '{named}', which this service does not implement. "
            + $"It implements: {string.Join(", ", implemented)}.";
        return Json(400, new { error }, error);
    }

    // Refused before any outbound call, which is the whole reason a kind declares its requirements
    // instead of the code knowing them.
    private static string? UnmetRequirement(
        ResolvedKind? kind, IReadOnlyDictionary<string, JsonElement> effective)
    {
        var missing = EndpointKindResolver.MissingRequirements(kind, effective);
        return missing.Count == 0
            ? null
            : $"Endpoint reaches kind '{kind!.Name}' but does not supply: {string.Join(", ", missing)}.";
    }

    public async Task<EndpointCallResult> ExecuteAsync(EndpointCallRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.EndpointName))
            return Json(400, new { error = "Request must include a non-empty endpointName." }, "Request must include a non-empty endpointName.");

        var thing = await _mycelium.FindThingByNameAsync(request.EndpointName);
        if (thing == null)
            return Json(404, new { error = $"Endpoint thing not found: {request.EndpointName}" }, $"Endpoint thing not found: {request.EndpointName}");

        var effective = await _mycelium.GetEffectivePropertiesAsync(thing.Value.Id);
        if (effective == null)
            return Problem(500, "Endpoint resolution failed", "Failed to resolve effective properties for endpoint thing.");

        // ---- The kinds this endpoint reaches ----
        // Reaching no kind for a role is a valid answer meaning "the plain behaviour": a plain body,
        // no credential, no paging. Reaching one nothing here implements is not, and says so.
        if (await ReadEndpointAsync(thing.Value.Id, cancellationToken) is not { } endpoint)
            return Problem(502, "Endpoint resolution failed",
                "Failed to read which kinds the endpoint reaches. Refusing rather than calling out as though it reaches none.");

        var kinds = endpoint.Kinds;
        kinds.TryGetValue(EndpointKindRoles.ResponseBody, out var bodyKind);
        kinds.TryGetValue(EndpointKindRoles.Authentication, out var authKind);
        kinds.TryGetValue(EndpointKindRoles.Paging, out var pagingKind);
        kinds.TryGetValue(EndpointKindRoles.Caching, out var cachingKind);

        foreach (var kind in new[] { bodyKind, authKind, pagingKind, cachingKind })
            if (UnmetRequirement(kind, effective) is { } unmet)
                return Json(400, new { error = unmet }, unmet);

        // A byte-level read wrapped in a base64 envelope; combinations that presuppose a decodable
        // string body (transforms, offset paging) are rejected before any side effect below.
        var binaryResponse = false;
        switch (bodyKind?.Name)
        {
            case null:
            case JsonBodyMechanism:
                break;
            case BinaryBodyMechanism:
                binaryResponse = true;
                break;
            default:
                return UnimplementedKind(EndpointKindRoles.ResponseBody, bodyKind.Name,
                    JsonBodyMechanism, BinaryBodyMechanism);
        }

        List<string>? urlConflicts = null;
        List<string>? methodConflicts = null;
        List<string>? transformConflicts = null;

        if (!EffectivePropertyResolver.TryGetEffectiveProperty(effective, "url", out var urlElement, out urlConflicts) ||
            !EffectivePropertyResolver.TryGetEffectiveProperty(effective, "httpMethod", out var methodElement, out methodConflicts))
        {
            if (urlConflicts != null || methodConflicts != null)
                return Json(400, new
                {
                    error = "Endpoint thing has ambiguous properties for url/httpMethod.",
                    conflicts = new { url = urlConflicts, httpMethod = methodConflicts }
                }, "Endpoint thing has ambiguous properties for url/httpMethod.");

            return Json(400, new { error = "Endpoint thing is missing required properties: url, httpMethod" },
                "Endpoint thing is missing required properties: url, httpMethod");
        }

        string? registeredTransform = null;
        if (EffectivePropertyResolver.TryGetEffectiveProperty(effective, "responseTransform", out var transformElement, out transformConflicts))
        {
            registeredTransform = transformElement.ValueKind == JsonValueKind.String
                ? transformElement.GetString()
                : transformElement.ToString();
        }

        if (transformConflicts != null)
            return Json(400, new
            {
                error = "Endpoint thing has ambiguous properties for responseTransform.",
                conflicts = new { responseTransform = transformConflicts }
            }, "Endpoint thing has ambiguous properties for responseTransform.");

        // What the endpoint reshapes with on this call. A request-supplied expression wins for this
        // call alone and is never written back: Tributary reads a source, and a read that rewrote
        // its own registration would change what every later caller of that source receives.
        var reshapeExpression = string.IsNullOrWhiteSpace(request.ResponseTransform)
            ? registeredTransform
            : request.ResponseTransform;

        if (!string.IsNullOrWhiteSpace(registeredTransform))
            _logger.LogInformation("Endpoint {EndpointName} responseTransform: {Transform}", request.EndpointName, registeredTransform);

        if (binaryResponse && !string.IsNullOrWhiteSpace(reshapeExpression))
            return Json(400, new { error = BinaryTransformClash }, BinaryTransformClash);

        // Compiled before the outbound call so an expression that cannot parse costs the source
        // nothing, whichever side supplied it.
        JsonataTransform? reshape = null;
        if (!string.IsNullOrWhiteSpace(reshapeExpression))
        {
            try
            {
                reshape = new JsonataTransform(reshapeExpression!);
            }
            catch (Exception ex)
            {
                return Json(400,
                    new { error = "Invalid responseTransform JSONata expression.", detail = ex.Message },
                    "Invalid responseTransform JSONata expression.");
            }
        }

        var url = urlElement.ValueKind == JsonValueKind.String ? urlElement.GetString() : urlElement.ToString();
        var method = methodElement.ValueKind == JsonValueKind.String ? methodElement.GetString() : methodElement.ToString();

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(method))
            return Json(400, new { error = "Endpoint url/httpMethod must be non-empty strings." }, "Endpoint url/httpMethod must be non-empty strings.");

        // Before the address is parsed, so an address still carrying a placeholder cannot become a
        // Uri that looks callable.
        url = AddressPlaceholders.Fill(url!, request.AddressParameters, out var unfilledPlaceholders);
        if (unfilledPlaceholders.Count > 0)
        {
            var unfilled = $"Endpoint url has placeholders with no value in addressParameters: "
                + $"{string.Join(", ", unfilledPlaceholders)}.";
            return Json(400, new { error = unfilled, unfilledPlaceholders }, unfilled);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var endpointUri))
            return Json(400, new { error = $"Invalid endpoint url: {url}" }, $"Invalid endpoint url: {url}");

        var normalizedMethod = method!.Trim().ToUpperInvariant();
        if (!HttpMethodValidator.IsSupportedMethod(normalizedMethod))
            return Json(400, new { error = $"Unsupported httpMethod: {method}" }, $"Unsupported httpMethod: {method}");

        if (!TryResolveOptionalMap(effective, "headers", out var headers, out var headersError))
            return EndpointCallResult.Failure(headersError!);
        if (!TryResolveOptionalMap(effective, "queryParams", out var queryParams, out var queryError))
            return EndpointCallResult.Failure(queryError!);

        var requestContentType = OutboundRequest.DefaultContentType;
        if (EffectivePropertyResolver.TryGetEffectiveProperty(effective, "requestContentType", out var ctElement, out var ctConflicts))
        {
            var ct = ctElement.ValueKind == JsonValueKind.String ? ctElement.GetString() : ctElement.ToString();
            if (!string.IsNullOrWhiteSpace(ct))
                requestContentType = ct;
        }
        if (ctConflicts != null)
            return EndpointCallResult.Failure(AmbiguousProperty("requestContentType", ctConflicts));

        // Optional Accept for content-negotiating upstreams (e.g. image/tiff from an ImageServer);
        // a dedicated structural key like requestContentType, not an entry in the headers map.
        if (!TryResolveOptionalString(effective, "acceptHeader", out var acceptHeader, out var acceptHeaderError))
            return EndpointCallResult.Failure(acceptHeaderError!);

        var timeout = OutboundRequest.DefaultTimeout;
        if (EffectivePropertyResolver.TryGetEffectiveProperty(effective, "timeout", out var timeoutElement, out var timeoutConflicts))
            timeout = OutboundRequest.ResolveTimeout(timeoutElement);
        if (timeoutConflicts != null)
            return EndpointCallResult.Failure(AmbiguousProperty("timeout", timeoutConflicts));

        // ---- Authentication (Task #5470) ----
        // Reaching no kind is a plain REST call. TokenExchangeAuth uses a pre-minted token, or one
        // minted from a configured credential exchange (token endpoint, form fields, and response
        // token/expiry paths all come from the template, so nothing here is source-specific). The
        // credential attaches as a query parameter (default `token`) or, when tokenHeader is set, a
        // request header. What the kind requires has already been checked above; the mint network call
        // is deferred into the try below so its failures become 502 rather than 400.
        string? preMintedToken = null;
        TokenExchangeRequest? tokenFetch = null;
        var tokenParam = "token";
        string? tokenHeader = null;
        string? tokenScheme = null;
        switch (authKind?.Name)
        {
            case null:
                break;
            case TokenExchangeMechanism:
                if (!TryResolveOptionalString(effective, "tokenParam", out var tp, out var tpError))
                    return EndpointCallResult.Failure(tpError!);
                if (!string.IsNullOrWhiteSpace(tp))
                    tokenParam = tp!.Trim();
                if (!TryResolveOptionalString(effective, "tokenHeader", out tokenHeader, out var thError))
                    return EndpointCallResult.Failure(thError!);
                if (!TryResolveOptionalString(effective, "tokenScheme", out tokenScheme, out var tschError))
                    return EndpointCallResult.Failure(tschError!);

                if (!TryResolveOptionalString(effective, "token", out var preMinted, out var tokenError))
                    return EndpointCallResult.Failure(tokenError!);
                if (!string.IsNullOrWhiteSpace(preMinted))
                {
                    preMintedToken = preMinted;
                    break;
                }

                if (!TryResolveOptionalString(effective, "tokenUrl", out var tokenUrl, out var urlErr))
                    return EndpointCallResult.Failure(urlErr!);
                if (!TryResolveOptionalMap(effective, "tokenRequest", out var tokenRequest, out var trError))
                    return EndpointCallResult.Failure(trError!);
                if (!TryResolveOptionalString(effective, "tokenPath", out var tokenPath, out var pathError))
                    return EndpointCallResult.Failure(pathError!);
                if (!TryResolveOptionalString(effective, "expiryPath", out var expiryPath, out var epError))
                    return EndpointCallResult.Failure(epError!);
                if (!TryResolveOptionalString(effective, "expiryUnit", out var expiryUnit, out var euError))
                    return EndpointCallResult.Failure(euError!);
                if (string.IsNullOrWhiteSpace(tokenUrl) || tokenRequest == null || tokenRequest.Count == 0 || string.IsNullOrWhiteSpace(tokenPath))
                {
                    // Reached only when the kind declares fewer requirements than the mechanism needs.
                    // The kind's own check above is the one an endpoint author sees; this guards the
                    // mechanism against a kind that under-declares.
                    const string underDeclared =
                        "Token exchange needs tokenUrl, tokenRequest and tokenPath, or a pre-minted token.";
                    return Json(400, new { error = underDeclared }, underDeclared);
                }
                tokenFetch = new TokenExchangeRequest(tokenUrl!, tokenRequest, tokenPath!, expiryPath, expiryUnit);
                break;
            default:
                return UnimplementedKind(EndpointKindRoles.Authentication, authKind.Name, TokenExchangeMechanism);
        }

        // ---- Paging (Task #5470) ----
        OffsetPaginationConfig? pageConfig = null;
        switch (pagingKind?.Name)
        {
            case null:
                break;
            case OffsetPagingMechanism:
                if (!TryResolveOptionalString(effective, "offsetParam", out var offsetParam, out var offsetError))
                    return EndpointCallResult.Failure(offsetError!);
                if (!TryResolveOptionalString(effective, "pageSizeParam", out var pageSizeParam, out var pspError))
                    return EndpointCallResult.Failure(pspError!);
                if (!TryResolveOptionalString(effective, "hasMorePath", out var hasMorePath, out var hmError))
                    return EndpointCallResult.Failure(hmError!);
                if (!TryResolveOptionalString(effective, "itemsPath", out var itemsPath, out var ipError))
                    return EndpointCallResult.Failure(ipError!);
                if (string.IsNullOrWhiteSpace(offsetParam) || string.IsNullOrWhiteSpace(hasMorePath) || string.IsNullOrWhiteSpace(itemsPath))
                {
                    const string underDeclared =
                        "Offset paging needs offsetParam, hasMorePath and itemsPath.";
                    return Json(400, new { error = underDeclared }, underDeclared);
                }

                int? pageSize = null;
                if (EffectivePropertyResolver.TryGetEffectiveProperty(effective, "pageSize", out var pageSizeElement, out var pageSizeConflicts))
                {
                    var rawPageSize = pageSizeElement.ValueKind == JsonValueKind.String ? pageSizeElement.GetString() : pageSizeElement.GetRawText();
                    if (!string.IsNullOrWhiteSpace(rawPageSize)
                        && int.TryParse(rawPageSize, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ps) && ps > 0)
                        pageSize = ps;
                }
                if (pageSizeConflicts != null)
                    return EndpointCallResult.Failure(AmbiguousProperty("pageSize", pageSizeConflicts));

                pageConfig = new OffsetPaginationConfig(
                    offsetParam!,
                    string.IsNullOrWhiteSpace(pageSizeParam) ? null : pageSizeParam!.Trim(),
                    pageSize,
                    hasMorePath!,
                    itemsPath!);
                break;
            default:
                return UnimplementedKind(EndpointKindRoles.Paging, pagingKind.Name, OffsetPagingMechanism);
        }

        if (binaryResponse && pageConfig != null)
        {
            var clash = $"A '{bodyKind!.Name}' body cannot be read page by page as '{pagingKind!.Name}'.";
            return Json(400, new { error = clash }, clash);
        }

        // ---- Caching (#5918) ----
        // Reaching no kind means every call refetches — today's behaviour. DiskCache serves a
        // repeated fetch from local disk within cacheTtl; the combinations it cannot answer
        // honestly are refused before anything is fetched or written.
        TimeSpan? cacheFor = null;
        switch (cachingKind?.Name)
        {
            case null:
                break;
            case DiskCacheMechanism:
                if (!TryResolveOptionalString(effective, "cacheTtl", out var rawCacheTtl, out var cacheTtlError))
                    return EndpointCallResult.Failure(cacheTtlError!);
                if (!double.TryParse(rawCacheTtl, NumberStyles.Float, CultureInfo.InvariantCulture, out var cacheTtlSeconds)
                    || cacheTtlSeconds <= 0)
                {
                    const string badTtl = "cacheTtl must be a positive number of seconds.";
                    return Json(400, new { error = badTtl }, badTtl);
                }
                cacheFor = TimeSpan.FromSeconds(cacheTtlSeconds);
                break;
            default:
                return UnimplementedKind(EndpointKindRoles.Caching, cachingKind.Name, DiskCacheMechanism);
        }

        if (cacheFor != null && pageConfig != null)
        {
            var clash = $"A '{cachingKind!.Name}' response cannot be assembled page by page as '{pagingKind!.Name}'.";
            return Json(400, new { error = clash }, clash);
        }
        if (cacheFor != null && authKind != null)
        {
            // A credentialed response served from disk would answer a later call without its
            // credential; refused until a keying design justifies otherwise.
            var clash = $"A '{cachingKind!.Name}' response cannot be combined with '{authKind.Name}'.";
            return Json(400, new { error = clash }, clash);
        }
        if (cacheFor != null
            && OutboundRequest.MethodSupportsBody(normalizedMethod)
            && request.Body.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
        {
            const string bodyClash = "A request with an outbound body cannot be served from disk: the body is not part of the cache key.";
            return Json(400, new { error = bodyClash }, bodyClash);
        }

        try
        {
            // Mint the token now (deferred network call). Its own catch returns a generic 502 — the
            // upstream message can name the credential and must not leak; the real one is logged.
            var credential = preMintedToken;
            if (tokenFetch != null)
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
                if (!string.IsNullOrEmpty(tokenHeader))
                    effectiveHeaders[tokenHeader] = string.IsNullOrEmpty(tokenScheme) ? credential : $"{tokenScheme} {credential}";
                else
                    effectiveQueryParams[tokenParam] = credential;
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

            if (reshape != null && status is >= 200 and < 300)
            {
                var ingestResult = await _observationService.CreateObservationsAsync(
                    thing.Value.Id, reshape, body, request.SubjectId, endpoint.Observed);
                if (!ingestResult.Success)
                    return Json(400, new { error = ingestResult.Error, detail = ingestResult.Detail }, ingestResult.Error ?? "Observation ingest failed.");

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

    private static EndpointCallResult Json(int status, object body, string message) =>
        EndpointCallResult.Failure(new JsonError(status, body, message));

    private static EndpointCallResult Problem(int status, string title, string detail) =>
        EndpointCallResult.Failure(new ProblemError(status, title, detail));

    private static bool TryResolveOptionalMap(
        IReadOnlyDictionary<string, JsonElement> effective, string name, out Dictionary<string, string>? map, out EndpointCallError? error)
    {
        map = null;
        error = null;
        if (EffectivePropertyResolver.TryGetEffectiveProperty(effective, name, out var element, out var conflicts))
            map = OutboundRequest.TryParseStringMap(element);
        if (conflicts != null)
        {
            error = AmbiguousProperty(name, conflicts);
            return false;
        }
        return true;
    }

    private static bool TryResolveOptionalString(
        IReadOnlyDictionary<string, JsonElement> effective, string name, out string? value, out EndpointCallError? error)
    {
        value = null;
        error = null;
        if (EffectivePropertyResolver.TryGetEffectiveProperty(effective, name, out var element, out var conflicts))
            value = element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
        if (conflicts != null)
        {
            error = AmbiguousProperty(name, conflicts);
            return false;
        }
        return true;
    }

    private static EndpointCallError AmbiguousProperty(string name, List<string> conflicts) =>
        new JsonError(400, new
        {
            error = $"Endpoint thing has ambiguous properties for {name}.",
            conflicts = new Dictionary<string, List<string>> { [name] = conflicts }
        }, $"Endpoint thing has ambiguous properties for {name}.");

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
