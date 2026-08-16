using System.Globalization;
using System.Text.Json;
using vos.Service.Shared;
using Microsoft.Extensions.Logging;
using vos.Service.Tributary.Helpers;
using vos.Service.Tributary.Models;
using vos.Service.Shared.Subscriptions;
using vos.Service.Shared.Validation;

namespace vos.Service.Tributary.Services;

// Resolves an endpoint Thing's effective properties and performs the outbound HTTP call — auth-kinds,
// offset paging, binary response envelopes, and optional JSONata response transform / observation
// ingest. Extracted verbatim from the original /handle lambda so both the HTTP endpoint and the
// pipeline DAG node (TributaryNode, Feature #5628) run the identical path. Returns a framework-free
// EndpointCallResult; callers map it to their own response type.
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

    public EndpointCallService(
        MyceliumClient mycelium,
        IHttpClientFactory httpClientFactory,
        ObservationIngestService observationService,
        TokenExchangeCache tokenExchangeCache,
        ISubscriptionClient subscriptions,
        ILogger<EndpointCallService> logger)
    {
        _mycelium = mycelium;
        _httpClientFactory = httpClientFactory;
        _observationService = observationService;
        _tokenExchangeCache = tokenExchangeCache;
        _subscriptions = subscriptions;
        _logger = logger;
    }

    // One scoped read answers every role. Unsubscribing in a finally keeps a failed resolution from
    // leaving a live subscription on the gateway for the rest of the process's life.
    private async Task<IReadOnlyDictionary<string, ResolvedKind>> ResolveKindsAsync(
        Guid endpointId, CancellationToken cancellationToken)
    {
        var subscribed = await _subscriptions.SubscribeAsync(
            EndpointKindResolver.SelectorFor(endpointId), cancellationToken);
        try
        {
            return EndpointKindResolver.Resolve(subscribed.Snapshot, endpointId);
        }
        finally
        {
            await _subscriptions.UnsubscribeAsync(subscribed.SubscriptionId, cancellationToken);
        }
    }

    // Checked at two points on the same run, so it is written once. The two checks are not redundant:
    // a transform can arrive on the request or be declared on the endpoint, and only the second is
    // known once the endpoint's own properties have resolved.
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
        ResolvedKind? kind, Dictionary<string, JsonElement> effective)
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
        var kinds = await ResolveKindsAsync(thing.Value.Id, cancellationToken);
        kinds.TryGetValue(EndpointKindRoles.ResponseBody, out var bodyKind);
        kinds.TryGetValue(EndpointKindRoles.Authentication, out var authKind);
        kinds.TryGetValue(EndpointKindRoles.Paging, out var pagingKind);

        foreach (var kind in new[] { bodyKind, authKind, pagingKind })
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

        var hasOverrideTransform = !string.IsNullOrWhiteSpace(request.ResponseTransform);
        if (binaryResponse && hasOverrideTransform)
            return Json(400, new { error = BinaryTransformClash }, BinaryTransformClash);
        JsonataTransform? overrideQuery = null;
        if (hasOverrideTransform)
        {
            try
            {
                overrideQuery = new JsonataTransform(request.ResponseTransform!);
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

        if (binaryResponse && !string.IsNullOrWhiteSpace(responseTransform))
            return Json(400, new { error = BinaryTransformClash }, BinaryTransformClash);

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

            if (binaryResponse)
            {
                // Bytes must be read before any string decode: ReadAsStringAsync replaces non-UTF-8
                // sequences with U+FFFD, which is lossy and irreversible. The envelope stays
                // application/json so the result flows through existing proxying unchanged.
                var (_, bytes, upstreamContentType) = await CallEndpointBinaryAsync(
                    _httpClientFactory, endpointUri, normalizedMethod, request.Body, effectiveHeaders, effectiveQueryParams, requestContentType, acceptHeader, timeout, cancellationToken);

                var envelope = JsonSerializer.Serialize(new
                {
                    contentType = string.IsNullOrWhiteSpace(upstreamContentType) ? "application/octet-stream" : upstreamContentType,
                    dataBase64 = Convert.ToBase64String(bytes),
                    byteLength = bytes.Length
                });
                return EndpointCallResult.Body(envelope, "application/json");
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
                            _httpClientFactory, endpointUri, normalizedMethod, request.Body, effectiveHeaders, pageParams, requestContentType, acceptHeader, timeout, ct);
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
                    _httpClientFactory, endpointUri, normalizedMethod, request.Body, effectiveHeaders, effectiveQueryParams, requestContentType, acceptHeader, timeout, cancellationToken);
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
                JsonataTransform propertyQuery;
                try
                {
                    propertyQuery = new JsonataTransform(responseTransform!);
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
