using System.Globalization;
using System.Text.Json;
using Jsonata.Net.Native;
using Microsoft.Extensions.Logging;
using vos.ManagedMicroservice.Tributary.Helpers;
using vos.ManagedMicroservice.Tributary.Models;
using vos.ManagedMicroservice.Shared.Validation;

namespace vos.ManagedMicroservice.Tributary.Services;

/// <summary>
/// Resolves an endpoint Thing's effective properties and performs the outbound HTTP call — auth-kinds,
/// offset paging, and optional JSONata response transform / observation ingest. Extracted verbatim from
/// the original <c>/handle</c> lambda so both the HTTP endpoint and the pipeline DAG node
/// (<see cref="TributaryNode"/>, Feature #5628) run the identical path. Returns a framework-free
/// <see cref="EndpointCallResult"/>; callers map it to their own response type.
/// </summary>
public sealed class EndpointCallService
{
    private readonly MyceliumClient _mycelium;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ObservationIngestService _observationService;
    private readonly TokenExchangeCache _tokenExchangeCache;
    private readonly ILogger<EndpointCallService> _logger;

    public EndpointCallService(
        MyceliumClient mycelium,
        IHttpClientFactory httpClientFactory,
        ObservationIngestService observationService,
        TokenExchangeCache tokenExchangeCache,
        ILogger<EndpointCallService> logger)
    {
        _mycelium = mycelium;
        _httpClientFactory = httpClientFactory;
        _observationService = observationService;
        _tokenExchangeCache = tokenExchangeCache;
        _logger = logger;
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

        var hasOverrideTransform = !string.IsNullOrWhiteSpace(request.ResponseTransform);
        JsonataQuery? overrideQuery = null;
        if (hasOverrideTransform)
        {
            try
            {
                overrideQuery = new JsonataQuery(request.ResponseTransform!);
            }
            catch (Exception ex)
            {
                return Json(400, new { error = "Invalid responseTransform JSONata expression.", detail = ex.Message }, "Invalid responseTransform JSONata expression.");
            }

            var setOverride = await _mycelium.SetThingPropertyAsync(thing.Value.Id, "responseTransform", request.ResponseTransform);
            if (!setOverride)
                return Problem(502, "Endpoint update failed", "Failed to persist responseTransform override to endpoint thing.");
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

        string? responseTransform = null;
        if (EffectivePropertyResolver.TryGetEffectiveProperty(effective, "responseTransform", out var transformElement, out transformConflicts))
        {
            responseTransform = transformElement.ValueKind == JsonValueKind.String
                ? transformElement.GetString()
                : transformElement.ToString();
        }

        if (!string.IsNullOrWhiteSpace(responseTransform))
            _logger.LogInformation("Endpoint {EndpointName} responseTransform: {Transform}", request.EndpointName, responseTransform);

        if (transformConflicts != null)
            return Json(400, new
            {
                error = "Endpoint thing has ambiguous properties for responseTransform.",
                conflicts = new { responseTransform = transformConflicts }
            }, "Endpoint thing has ambiguous properties for responseTransform.");

        var url = urlElement.ValueKind == JsonValueKind.String ? urlElement.GetString() : urlElement.ToString();
        var method = methodElement.ValueKind == JsonValueKind.String ? methodElement.GetString() : methodElement.ToString();

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(method))
            return Json(400, new { error = "Endpoint url/httpMethod must be non-empty strings." }, "Endpoint url/httpMethod must be non-empty strings.");

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

        var timeout = OutboundRequest.DefaultTimeout;
        if (EffectivePropertyResolver.TryGetEffectiveProperty(effective, "timeout", out var timeoutElement, out var timeoutConflicts))
            timeout = OutboundRequest.ResolveTimeout(timeoutElement);
        if (timeoutConflicts != null)
            return EndpointCallResult.Failure(AmbiguousProperty("timeout", timeoutConflicts));

        // ---- Auth-kind branching (Task #5470) ----
        // authKind is a structural key on the root Endpoint template; descendants resolve its value.
        // none/absent -> a plain REST call. tokenExchange -> a pre-minted token, or one minted from a
        // configured credential exchange (token endpoint, form fields, and response token/expiry paths
        // all come from the template, so nothing here is source-specific). The credential attaches as a
        // query param (default `token`) or, when tokenHeader is set, a request header. Validation gaps
        // are 400 here; the mint network call is deferred into the try below so failures become 502.
        if (!TryResolveOptionalString(effective, "authKind", out var authKind, out var authKindError))
            return EndpointCallResult.Failure(authKindError!);

        string? preMintedToken = null;
        TokenExchangeRequest? tokenFetch = null;
        var tokenParam = "token";
        string? tokenHeader = null;
        string? tokenScheme = null;
        switch (authKind?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case "none":
                break;
            case "tokenexchange":
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
                    return Json(400, new { error = "authKind 'tokenExchange' requires tokenUrl, tokenRequest, and tokenPath (or a pre-minted token)." },
                        "authKind 'tokenExchange' requires tokenUrl, tokenRequest, and tokenPath (or a pre-minted token).");
                tokenFetch = new TokenExchangeRequest(tokenUrl!, tokenRequest, tokenPath!, expiryPath, expiryUnit);
                break;
            default:
                return Json(400, new { error = $"Unsupported authKind: {authKind}" }, $"Unsupported authKind: {authKind}");
        }

        // ---- Offset paging resolution (Task #5470) ----
        if (!TryResolveOptionalString(effective, "pagingKind", out var pagingKind, out var pagingError))
            return EndpointCallResult.Failure(pagingError!);

        OffsetPaginationConfig? pageConfig = null;
        switch (pagingKind?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case "none":
                break;
            case "offset":
                if (!TryResolveOptionalString(effective, "offsetParam", out var offsetParam, out var offsetError))
                    return EndpointCallResult.Failure(offsetError!);
                if (!TryResolveOptionalString(effective, "pageSizeParam", out var pageSizeParam, out var pspError))
                    return EndpointCallResult.Failure(pspError!);
                if (!TryResolveOptionalString(effective, "hasMorePath", out var hasMorePath, out var hmError))
                    return EndpointCallResult.Failure(hmError!);
                if (!TryResolveOptionalString(effective, "itemsPath", out var itemsPath, out var ipError))
                    return EndpointCallResult.Failure(ipError!);
                if (string.IsNullOrWhiteSpace(offsetParam) || string.IsNullOrWhiteSpace(hasMorePath) || string.IsNullOrWhiteSpace(itemsPath))
                    return Json(400, new { error = "pagingKind 'offset' requires offsetParam, hasMorePath, and itemsPath." },
                        "pagingKind 'offset' requires offsetParam, hasMorePath, and itemsPath.");

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
                return Json(400, new { error = $"Unsupported pagingKind: {pagingKind}" }, $"Unsupported pagingKind: {pagingKind}");
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

            int status;
            string body;
            string? contentType;

            if (pageConfig != null)
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

                        var (pageStatus, pageBody, _) = await CallEndpointAsync(
                            _httpClientFactory, endpointUri, normalizedMethod, request.Body, effectiveHeaders, pageParams, requestContentType, timeout, ct);
                        if (pageStatus is < 200 or >= 300)
                            throw new HttpRequestException($"Paged request failed with status {pageStatus} at {paging.OffsetParam}={offset}.");
                        return pageBody;
                    },
                    paging);
                status = 200;
                contentType = "application/json";
            }
            else
            {
                (status, body, contentType) = await CallEndpointAsync(
                    _httpClientFactory, endpointUri, normalizedMethod, request.Body, effectiveHeaders, effectiveQueryParams, requestContentType, timeout, cancellationToken);
            }

            if (hasOverrideTransform && overrideQuery != null && status is >= 200 and < 300)
            {
                var ingestResult = await _observationService.CreateObservationsAsync(thing.Value.Id, overrideQuery, body);
                if (!ingestResult.Success)
                    return Json(400, new { error = ingestResult.Error, detail = ingestResult.Detail }, ingestResult.Error ?? "Observation ingest failed.");

                return EndpointCallResult.Ingested(new IngestSummary(
                    thing.Value.Id, ingestResult.EntitiesTouched, ingestResult.ObservationsSubmitted));
            }

            if (!string.IsNullOrWhiteSpace(responseTransform) && status is >= 200 and < 300)
            {
                JsonataQuery propertyQuery;
                try
                {
                    propertyQuery = new JsonataQuery(responseTransform!);
                }
                catch (Exception ex)
                {
                    return Problem(502, "Endpoint response transform failed", ex.Message);
                }

                if (!_observationService.TryTransform(body, propertyQuery, out var transformed, out var transformError))
                    return Problem(502, "Endpoint response transform failed", transformError);

                return EndpointCallResult.Body(transformed, "application/json");
            }

            return EndpointCallResult.Body(body, contentType ?? "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Endpoint call failed for {EndpointName}", request.EndpointName);
            return Problem(502, "Endpoint call failed", ex.Message);
        }
    }

    private static EndpointCallResult Json(int status, object body, string message) =>
        EndpointCallResult.Failure(new JsonError(status, body, message));

    private static EndpointCallResult Problem(int status, string title, string detail) =>
        EndpointCallResult.Failure(new ProblemError(status, title, detail));

    private static bool TryResolveOptionalMap(
        Dictionary<string, JsonElement> effective, string name, out Dictionary<string, string>? map, out EndpointCallError? error)
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
        Dictionary<string, JsonElement> effective, string name, out string? value, out EndpointCallError? error)
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
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = timeout;

        using var request = OutboundRequest.Build(method, endpointUri, body, headers, queryParameters, requestContentType);

        using var response = await client.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.ToString();
        return ((int)response.StatusCode, content, contentType);
    }
}
