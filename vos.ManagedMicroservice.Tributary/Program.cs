using System.Globalization;
using System.Text.Json;
using vos.Auth.Shared;
using vos.ManagedMicroservice.Tributary.Configuration;
using vos.ManagedMicroservice.Tributary.Helpers;
using vos.ManagedMicroservice.Tributary.Models;
using vos.ManagedMicroservice.Tributary.Services;
using vos.ManagedMicroservice.Shared.Validation;
using Jsonata.Net.Native;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var cliArgs = CliArgs.Parse(args, builder.Configuration);
if (cliArgs == null)
{
    Console.WriteLine(CliArgs.UsageMessage);
    Environment.Exit(1);
    return;
}

var servicePort = cliArgs.Port;
var myceliumUrl = cliArgs.MyceliumUrl;
var serviceToken = cliArgs.Token;
var signingKey = cliArgs.SigningKey;

// Skip the file sink when running under WebApplicationFactory<Program> tests. Same
// rationale as Metabolism — file I/O under the test host has no value and invites flakiness.
var isTestingEnv = builder.Environment.IsEnvironment("Testing");

var loggerConfig = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "Tributary");

if (!isTestingEnv)
{
    var logPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "logs", "tributary-.log");
    loggerConfig = loggerConfig.WriteTo.File(
        path: logPath,
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
        shared: true);
}

Log.Logger = loggerConfig.CreateLogger();

try
{
    Log.Information("VillageOS Tributary Service - Port: {Port}, Mycelium: {MyceliumUrl}", servicePort, myceliumUrl);

    builder.Host.UseSerilog();
    builder.WebHost.UseUrls($"http://localhost:{servicePort}");
    builder.Services.AddHttpClient();

    // Add JWT auth if mycelium provided a signing key. Bug #5391: use the
    // issuer/audience Mycelium passes via CLI so validation matches what
    // Mycelium signed.
    var authEnabled = !string.IsNullOrEmpty(signingKey);
    if (authEnabled)
    {
        builder.AddMyceliumTokenAuth(
            signingKey!,
            issuer: cliArgs.Issuer ?? "VillageOS",
            audience: cliArgs.Audience ?? "VosClients");
        Log.Information("JWT authentication enabled for incoming mycelium requests (issuer={Issuer}, audience={Audience})",
            cliArgs.Issuer ?? "VillageOS", cliArgs.Audience ?? "VosClients");
    }

    builder.Services.AddSingleton(sp =>
        new MyceliumClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<MyceliumClient>>(),
            myceliumUrl,
            serviceToken));
    builder.Services.AddSingleton<IEndpointMyceliumClient>(sp => sp.GetRequiredService<MyceliumClient>());
    builder.Services.AddSingleton<ObservationIngestService>();
    // Per-process token-exchange cache (Task #5470). TimeProvider.System drives its refresh threshold;
    // tests substitute a fake clock. Singleton so the cache survives across /handle requests.
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<TokenExchangeCache>();

    var app = builder.Build();

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }

    var handleEndpoint = app.MapPost("/handle", async (
        EndpointCallRequest request,
        MyceliumClient myceliumClient,
        IHttpClientFactory httpClientFactory,
        ObservationIngestService observationService,
        TokenExchangeCache tokenExchangeCache) =>
    {
        if (string.IsNullOrWhiteSpace(request.EndpointName))
            return Results.BadRequest(new { error = "Request must include a non-empty endpointName." });

        var thing = await myceliumClient.FindThingByNameAsync(request.EndpointName);
        if (thing == null)
            return Results.NotFound(new { error = $"Endpoint thing not found: {request.EndpointName}" });

        var effective = await myceliumClient.GetEffectivePropertiesAsync(thing.Value.Id);
        if (effective == null)
        {
            return Results.Problem(
                detail: "Failed to resolve effective properties for endpoint thing.",
                statusCode: 500,
                title: "Endpoint resolution failed");
        }

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
                return Results.BadRequest(new
                {
                    error = "Invalid responseTransform JSONata expression.",
                    detail = ex.Message
                });
            }

            var setOverride = await myceliumClient.SetThingPropertyAsync(
                thing.Value.Id,
                "responseTransform",
                request.ResponseTransform);

            if (!setOverride)
            {
                return Results.Problem(
                    detail: "Failed to persist responseTransform override to endpoint thing.",
                    statusCode: 502,
                    title: "Endpoint update failed");
            }
        }

        List<string>? urlConflicts = null;
        List<string>? methodConflicts = null;
        List<string>? transformConflicts = null;

        if (!EffectivePropertyResolver.TryGetEffectiveProperty(effective, "url", out var urlElement, out urlConflicts) ||
            !EffectivePropertyResolver.TryGetEffectiveProperty(effective, "httpMethod", out var methodElement, out methodConflicts))
        {
            if (urlConflicts != null || methodConflicts != null)
            {
                return Results.BadRequest(new
                {
                    error = "Endpoint thing has ambiguous properties for url/httpMethod.",
                    conflicts = new
                    {
                        url = urlConflicts,
                        httpMethod = methodConflicts
                    }
                });
            }

            return Results.BadRequest(new
            {
                error = "Endpoint thing is missing required properties: url, httpMethod"
            });
        }

        string? responseTransform = null;
        if (EffectivePropertyResolver.TryGetEffectiveProperty(effective, "responseTransform", out var transformElement, out transformConflicts))
        {
            responseTransform = transformElement.ValueKind == JsonValueKind.String
                ? transformElement.GetString()
                : transformElement.ToString();
        }

        if (!string.IsNullOrWhiteSpace(responseTransform))
            Log.Information("Endpoint {EndpointName} responseTransform: {Transform}", request.EndpointName, responseTransform);

        if (transformConflicts != null)
        {
            return Results.BadRequest(new
            {
                error = "Endpoint thing has ambiguous properties for responseTransform.",
                conflicts = new
                {
                    responseTransform = transformConflicts
                }
            });
        }

        var url = urlElement.ValueKind == JsonValueKind.String ? urlElement.GetString() : urlElement.ToString();
        var method = methodElement.ValueKind == JsonValueKind.String ? methodElement.GetString() : methodElement.ToString();

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(method))
            return Results.BadRequest(new { error = "Endpoint url/httpMethod must be non-empty strings." });

        if (!Uri.TryCreate(url, UriKind.Absolute, out var endpointUri))
            return Results.BadRequest(new { error = $"Invalid endpoint url: {url}" });

        var normalizedMethod = method!.Trim().ToUpperInvariant();
        if (!HttpMethodValidator.IsSupportedMethod(normalizedMethod))
            return Results.BadRequest(new { error = $"Unsupported httpMethod: {method}" });

        if (!TryResolveOptionalMap(effective, "headers", out var headers, out var headersError))
            return headersError!;
        if (!TryResolveOptionalMap(effective, "queryParams", out var queryParams, out var queryError))
            return queryError!;

        var requestContentType = OutboundRequest.DefaultContentType;
        if (EffectivePropertyResolver.TryGetEffectiveProperty(effective, "requestContentType", out var ctElement, out var ctConflicts))
        {
            var ct = ctElement.ValueKind == JsonValueKind.String ? ctElement.GetString() : ctElement.ToString();
            if (!string.IsNullOrWhiteSpace(ct))
                requestContentType = ct;
        }
        if (ctConflicts != null)
            return AmbiguousProperty("requestContentType", ctConflicts);

        var timeout = OutboundRequest.DefaultTimeout;
        if (EffectivePropertyResolver.TryGetEffectiveProperty(effective, "timeout", out var timeoutElement, out var timeoutConflicts))
            timeout = OutboundRequest.ResolveTimeout(timeoutElement);
        if (timeoutConflicts != null)
            return AmbiguousProperty("timeout", timeoutConflicts);

        // ---- Auth-kind branching (Task #5470) ----
        // authKind is a structural key on the root Endpoint template; descendants resolve its value.
        // none/absent -> a plain REST call. tokenExchange -> a pre-minted token, or one minted from a
        // configured credential exchange (token endpoint, form fields, and response token/expiry paths
        // all come from the template, so nothing here is source-specific). The credential attaches as a
        // query param (default `token`) or, when tokenHeader is set, a request header. Validation gaps
        // are 400 here; the mint network call is deferred into the try below so failures become 502.
        if (!TryResolveOptionalString(effective, "authKind", out var authKind, out var authKindError))
            return authKindError!;

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
                    return tpError!;
                if (!string.IsNullOrWhiteSpace(tp))
                    tokenParam = tp!.Trim();
                if (!TryResolveOptionalString(effective, "tokenHeader", out tokenHeader, out var thError))
                    return thError!;
                if (!TryResolveOptionalString(effective, "tokenScheme", out tokenScheme, out var tschError))
                    return tschError!;

                if (!TryResolveOptionalString(effective, "token", out var preMinted, out var tokenError))
                    return tokenError!;
                if (!string.IsNullOrWhiteSpace(preMinted))
                {
                    preMintedToken = preMinted;
                    break;
                }

                if (!TryResolveOptionalString(effective, "tokenUrl", out var tokenUrl, out var urlError))
                    return urlError!;
                if (!TryResolveOptionalMap(effective, "tokenRequest", out var tokenRequest, out var trError))
                    return trError!;
                if (!TryResolveOptionalString(effective, "tokenPath", out var tokenPath, out var pathError))
                    return pathError!;
                if (!TryResolveOptionalString(effective, "expiryPath", out var expiryPath, out var epError))
                    return epError!;
                if (!TryResolveOptionalString(effective, "expiryUnit", out var expiryUnit, out var euError))
                    return euError!;
                if (string.IsNullOrWhiteSpace(tokenUrl) || tokenRequest == null || tokenRequest.Count == 0 || string.IsNullOrWhiteSpace(tokenPath))
                    return Results.BadRequest(new { error = "authKind 'tokenExchange' requires tokenUrl, tokenRequest, and tokenPath (or a pre-minted token)." });
                tokenFetch = new TokenExchangeRequest(tokenUrl!, tokenRequest, tokenPath!, expiryPath, expiryUnit);
                break;
            default:
                return Results.BadRequest(new { error = $"Unsupported authKind: {authKind}" });
        }

        // ---- Offset paging resolution (Task #5470) ----
        if (!TryResolveOptionalString(effective, "pagingKind", out var pagingKind, out var pagingError))
            return pagingError!;

        OffsetPaginationConfig? pageConfig = null;
        switch (pagingKind?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case "none":
                break;
            case "offset":
                if (!TryResolveOptionalString(effective, "offsetParam", out var offsetParam, out var offsetError))
                    return offsetError!;
                if (!TryResolveOptionalString(effective, "pageSizeParam", out var pageSizeParam, out var pspError))
                    return pspError!;
                if (!TryResolveOptionalString(effective, "hasMorePath", out var hasMorePath, out var hmError))
                    return hmError!;
                if (!TryResolveOptionalString(effective, "itemsPath", out var itemsPath, out var ipError))
                    return ipError!;
                if (string.IsNullOrWhiteSpace(offsetParam) || string.IsNullOrWhiteSpace(hasMorePath) || string.IsNullOrWhiteSpace(itemsPath))
                    return Results.BadRequest(new { error = "pagingKind 'offset' requires offsetParam, hasMorePath, and itemsPath." });

                int? pageSize = null;
                if (EffectivePropertyResolver.TryGetEffectiveProperty(effective, "pageSize", out var pageSizeElement, out var pageSizeConflicts))
                {
                    var rawPageSize = pageSizeElement.ValueKind == JsonValueKind.String ? pageSizeElement.GetString() : pageSizeElement.GetRawText();
                    if (!string.IsNullOrWhiteSpace(rawPageSize)
                        && int.TryParse(rawPageSize, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ps) && ps > 0)
                        pageSize = ps;
                }
                if (pageSizeConflicts != null)
                    return AmbiguousProperty("pageSize", pageSizeConflicts);

                pageConfig = new OffsetPaginationConfig(
                    offsetParam!,
                    string.IsNullOrWhiteSpace(pageSizeParam) ? null : pageSizeParam!.Trim(),
                    pageSize,
                    hasMorePath!,
                    itemsPath!);
                break;
            default:
                return Results.BadRequest(new { error = $"Unsupported pagingKind: {pagingKind}" });
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
                    credential = await tokenExchangeCache.GetTokenAsync(tokenFetch);
                }
                catch (Exception tokenEx)
                {
                    Log.Error(tokenEx, "Token acquisition failed for {EndpointName}", request.EndpointName);
                    return Results.Problem(
                        detail: "Failed to obtain an authentication token for the endpoint.",
                        statusCode: 502,
                        title: "Token acquisition failed");
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
                            httpClientFactory, endpointUri, normalizedMethod, request.Body, effectiveHeaders, pageParams, requestContentType, timeout);
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
                    httpClientFactory,
                    endpointUri,
                    normalizedMethod,
                    request.Body,
                    effectiveHeaders,
                    effectiveQueryParams,
                    requestContentType,
                    timeout);
            }

            if (hasOverrideTransform && overrideQuery != null && status is >= 200 and < 300)
            {
                var ingestResult = await observationService.CreateObservationsAsync(thing.Value.Id, overrideQuery, body);
                if (!ingestResult.Success)
                {
                    return Results.BadRequest(new
                    {
                        error = ingestResult.Error,
                        detail = ingestResult.Detail,
                        index = ingestResult.Index,
                        observationThingId = ingestResult.ObservationThingId
                    });
                }

                return Results.Ok(new
                {
                    success = true,
                    endpointThingId = thing.Value.Id,
                    observedCount = ingestResult.ObservedCount,
                    message = "Observations created and related to endpoint."
                });
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
                    return Results.Problem(
                        detail: ex.Message,
                        statusCode: 502,
                        title: "Endpoint response transform failed");
                }

                if (!observationService.TryTransform(body, propertyQuery, out var transformed, out var transformError))
                {
                    return Results.Problem(
                        detail: transformError,
                        statusCode: 502,
                        title: "Endpoint response transform failed");
                }

                return Results.Content(transformed, "application/json");
            }

            return Results.Content(body, contentType ?? "application/json");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Endpoint call failed for {EndpointName}", request.EndpointName);
            return Results.Problem(
                detail: ex.Message,
                statusCode: 502,
                title: "Endpoint call failed");
        }
    });
    if (authEnabled) handleEndpoint.RequireAuthorization();

    app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "Tributary" }));

    var shutdownEndpoint = app.MapPost("/shutdown", (IHostApplicationLifetime lifetime) =>
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(300);
            lifetime.StopApplication();
        });

        return Results.Ok(new { message = "Shutting down Tributary" });
    });
    if (authEnabled) shutdownEndpoint.RequireAuthorization();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Tributary terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

static bool TryResolveOptionalMap(
    Dictionary<string, JsonElement> effective,
    string name,
    out Dictionary<string, string>? map,
    out IResult? error)
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

static bool TryResolveOptionalString(
    Dictionary<string, JsonElement> effective,
    string name,
    out string? value,
    out IResult? error)
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

static IResult AmbiguousProperty(string name, List<string> conflicts) =>
    Results.BadRequest(new
    {
        error = $"Endpoint thing has ambiguous properties for {name}.",
        conflicts = new Dictionary<string, List<string>> { [name] = conflicts }
    });

static async Task<(int StatusCode, string Body, string? ContentType)> CallEndpointAsync(
    IHttpClientFactory httpClientFactory,
    Uri endpointUri,
    string method,
    JsonElement body,
    IReadOnlyDictionary<string, string>? headers,
    IReadOnlyDictionary<string, string>? queryParameters,
    string requestContentType,
    TimeSpan timeout)
{
    var client = httpClientFactory.CreateClient();
    client.Timeout = timeout;

    using var request = OutboundRequest.Build(method, endpointUri, body, headers, queryParameters, requestContentType);

    using var response = await client.SendAsync(request);
    var content = await response.Content.ReadAsStringAsync();
    var contentType = response.Content.Headers.ContentType?.ToString();
    return ((int)response.StatusCode, content, contentType);
}

// Exposed to WebApplicationFactory<Program> in the test project per docs/MICROSERVICES.md.
// Top-level statements compile to a `Program` class that is internal by default — this empty
// partial declaration just elevates it to public so the test factory can name it.
public partial class Program { }
