using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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
    private readonly ProviderUnavailability _unavailability;
    private readonly PassedOnRequestLimit _passedOnRequestLimit;
    private readonly TimeProvider _clock;

    public EndpointCallService(
        MyceliumClient mycelium,
        IHttpClientFactory httpClientFactory,
        ObservationIngestService observationService,
        TokenExchangeCache tokenExchangeCache,
        ISubscriptionClient subscriptions,
        DiskResponseCache diskCache,
        ProviderUnavailability unavailability,
        PassedOnRequestLimit passedOnRequestLimit,
        TimeProvider clock,
        ILogger<EndpointCallService> logger)
    {
        _mycelium = mycelium;
        _httpClientFactory = httpClientFactory;
        _observationService = observationService;
        _tokenExchangeCache = tokenExchangeCache;
        _subscriptions = subscriptions;
        _diskCache = diskCache;
        _unavailability = unavailability;
        _passedOnRequestLimit = passedOnRequestLimit;
        _clock = clock;
        _logger = logger;
    }

    // One scoped read answers every role, and the `observed` relationships the endpoint already carries with
    // them — those relationships are in the same snapshot whether or not anything reads them, so provenance
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
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

    // A call cut short by the deadline answers with what it was waiting on, so Mycelium is told why instead
    // of giving up on the request.
    public async Task<EndpointCallResult> ExecuteAsync(EndpointCallRequest request, PassedOnRequestDeadline deadline)
    {
        EndpointCallResult answer;
        try
        {
            answer = await AnswerAsync(request, deadline);
        }
        catch (OperationCanceledException) when (deadline.HasPassed)
        {
            return AtTheDeadline(request, deadline);
        }

        // An answer carrying no error holds the data source's own words or what was written, so it stands.
        return deadline.HasPassed && answer.Error is not null ? AtTheDeadline(request, deadline) : answer;
    }

    private EndpointCallResult AtTheDeadline(EndpointCallRequest request, PassedOnRequestDeadline deadline)
    {
        var reason = deadline.Reason(request.EndpointName);
        _logger.LogWarning("Answered the request for endpoint {EndpointName} at its deadline: {Reason}",
            request.EndpointName, reason);
        return Problem(504, "No answer before the deadline", reason);
    }

    private async Task<EndpointCallResult> AnswerAsync(EndpointCallRequest request, PassedOnRequestDeadline deadline)
    {
        var cancellationToken = deadline.Token;

        if (string.IsNullOrWhiteSpace(request.EndpointName))
            return Refusal.BadRequest("Request must include a non-empty endpointName.").ToResult();

        var thing = await _mycelium.FindThingByNameAsync(request.EndpointName, cancellationToken);
        if (thing == null)
            return new Refusal(404, $"Endpoint thing not found: {request.EndpointName}").ToResult();

        var effective = await _mycelium.GetEffectivePropertiesAsync(thing.Value.Id, cancellationToken);
        if (effective == null)
            return Problem(500, "Endpoint resolution failed", "Failed to resolve effective properties for endpoint thing.");

        if (await ReadEndpointAsync(thing.Value.Id, cancellationToken) is not { } endpoint)
            return Problem(502, "Endpoint resolution failed",
                "Failed to read which kinds the endpoint reaches. Refusing rather than calling out as though it reaches none.");

        if (!TryPlanCall(request, thing.Value.Id, effective, EndpointKinds.Reached(endpoint.Kinds), out var plan, out var refusal))
            return refusal.ToResult();

        deadline.Awaiting = AwaitedStep.DataSource;

        try
        {
            var (outbound, tokenFailure) = await PrepareOutboundAsync(request, plan, deadline);
            if (outbound is null)
                return tokenFailure!;

            if (plan.BinaryResponse)
                return await CallBinaryAsync(request, plan, outbound, thing.Value.Id, endpoint.Observed, deadline);
            if (outbound.Cached is { } cachedText)
                return await AnswerTextAsync(request, plan, thing.Value.Id, endpoint.Observed,
                    new ProviderAnswer<string>(200, Encoding.UTF8.GetString(cachedText.Bytes), cachedText.ContentType, null), deadline);
            if (plan.Paging != null)
                return await CallPagedAsync(request, plan, outbound, thing.Value.Id, endpoint.Observed, deadline);
            return await CallOnceAsync(request, plan, outbound, thing.Value.Id, endpoint.Observed, deadline);
        }
        catch (Exception exception) when (!deadline.HasPassed)
        {
            _logger.LogError(exception, "Endpoint call failed for {EndpointName}", request.EndpointName);
            return Problem(502, "Endpoint call failed", exception.Message);
        }
    }

    // What a kind requires is checked before any mechanism reads its settings, so an endpoint
    // that under-supplies is refused in the kind's words rather than the mechanism's. The body
    // kind comes before the reshape, which refuses a transform over bytes. Each clash check comes
    // after the last thing it combines, so an unimplemented kind is refused as such and never as
    // a clash with something it is not. Everything else is in the order the properties are read.
    private bool TryPlanCall(
        EndpointCallRequest request,
        Guid endpointThingId,
        IReadOnlyDictionary<string, JsonElement> effective,
        EndpointKinds kinds,
        [NotNullWhen(true)] out EndpointCallPlan? plan,
        [NotNullWhen(false)] out Refusal? refusal)
    {
        plan = null;

        refusal = EndpointCallChecks.UnmetRequirement(kinds, effective);
        if (refusal is not null)
            return false;
        if (!EndpointCallChecks.TryBodyKind(kinds.Body, out var binaryResponse, out refusal))
            return false;
        if (!EndpointCallChecks.TryAddress(effective, request.AddressParameters, out var address, out refusal))
            return false;
        if (!EndpointCallChecks.TryReshape(effective, request.ResponseTransform, binaryResponse, out var reshape, out refusal))
            return false;
        if (!string.IsNullOrWhiteSpace(reshape.Registered))
            _logger.LogInformation("Endpoint {EndpointName} responseTransform: {Transform}", request.EndpointName, reshape.Registered);
        if (!EndpointCallChecks.TryOptionalMap(effective, "headers", out var headers, out refusal))
            return false;
        if (!EndpointCallChecks.TryOptionalMap(effective, "queryParams", out var queryParameters, out refusal))
            return false;
        if (!EndpointCallChecks.TryRequestContentType(effective, out var requestContentType, out refusal))
            return false;
        // Optional Accept for content-negotiating upstreams (e.g. image/tiff from an ImageServer);
        // a dedicated structural key like requestContentType, not an entry in the headers map.
        if (!EndpointCallChecks.TryOptionalText(effective, "acceptHeader", out var acceptHeader, out refusal))
            return false;
        if (!EndpointCallChecks.TryTimeout(effective, out var statedTimeout, out refusal))
            return false;
        if (statedTimeout is { } stated)
            _passedOnRequestLimit.WarnOnceWhenNotShorter(endpointThingId, request.EndpointName!, stated);
        if (!EndpointCallChecks.TryUnavailability(effective, _unavailability, out var unavailability, out refusal))
            return false;
        if (!EndpointCallChecks.TryCredential(kinds.Authentication, effective, out var credential, out refusal))
            return false;
        if (!EndpointCallChecks.TryPaging(kinds.Paging, effective, out var paging, out refusal))
            return false;
        refusal = EndpointCallChecks.PagingClash(kinds, binaryResponse, paging);
        if (refusal is not null)
            return false;
        if (!EndpointCallChecks.TryCaching(kinds.Caching, effective, out var cacheFor, out refusal))
            return false;
        refusal = EndpointCallChecks.CachingClash(kinds, cacheFor, paging, address.Method, request.Body);
        if (refusal is not null)
            return false;
        if (!EndpointCallChecks.TryKeeping(kinds.Keeping, effective, request.AddressParameters, out var keep, out refusal))
            return false;
        refusal = EndpointCallChecks.KeepingClash(kinds, keep, paging);
        if (refusal is not null)
            return false;

        plan = new EndpointCallPlan(
            address, binaryResponse, reshape, headers, queryParameters, requestContentType, acceptHeader,
            statedTimeout ?? OutboundRequest.DefaultTimeout, unavailability, credential, paging, cacheFor, keep);
        return true;
    }

    private sealed record EndpointCallPlan(
        EndpointAddress Address,
        bool BinaryResponse,
        Reshape Reshape,
        IReadOnlyDictionary<string, string>? Headers,
        IReadOnlyDictionary<string, string>? QueryParameters,
        string RequestContentType,
        string? AcceptHeader,
        TimeSpan Timeout,
        ProviderUnavailability Unavailability,
        Credential? Credential,
        OffsetPaginationConfig? Paging,
        TimeSpan? CacheFor,
        KeepPlan? Keep);

    private async Task<(OutboundCall? Call, EndpointCallResult? TokenFailure)> PrepareOutboundAsync(
        EndpointCallRequest request, EndpointCallPlan plan, PassedOnRequestDeadline deadline)
    {
        // Mint the token now (deferred network call). Its own catch returns a generic 502 — the
        // upstream message can name the credential and must not leak; the real one is logged.
        var credential = plan.Credential?.PreMinted;
        if (plan.Credential?.Fetch is { } tokenFetch)
        {
            try
            {
                credential = await _tokenExchangeCache.GetTokenAsync(tokenFetch, deadline.Token);
            }
            catch (Exception exception) when (!deadline.HasPassed)
            {
                _logger.LogError(exception, "Token acquisition failed for {EndpointName}", request.EndpointName);
                return (null, Problem(502, "Token acquisition failed", "Failed to obtain an authentication token for the endpoint."));
            }
        }

        var queryParameters = plan.QueryParameters != null
            ? new Dictionary<string, string>(plan.QueryParameters, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var headers = plan.Headers != null
            ? new Dictionary<string, string>(plan.Headers, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(credential))
        {
            if (!string.IsNullOrEmpty(plan.Credential!.Header))
                headers[plan.Credential.Header] = string.IsNullOrEmpty(plan.Credential.Scheme)
                    ? credential
                    : $"{plan.Credential.Scheme} {credential}";
            else
                queryParameters[plan.Credential.Param] = credential;
        }

        // The key is the fully-resolved address — placeholders filled, query attached — plus
        // the Accept header, since the same address can answer with different formats.
        string? cacheKey = null;
        CachedResponse? cached = null;
        if (plan.CacheFor is { } timeToLive)
        {
            cacheKey = DiskResponseCache.CacheKey(
                OutboundRequest.ApplyQueryParameters(plan.Address.Uri, queryParameters), plan.AcceptHeader);
            cached = _diskCache.TryRead(request.EndpointName!, cacheKey, timeToLive);
        }

        return (new OutboundCall(headers, queryParameters, cacheKey, cached), null);
    }

    private sealed record OutboundCall(
        Dictionary<string, string> Headers,
        Dictionary<string, string> QueryParameters,
        string? CacheKey,
        CachedResponse? Cached);

    private async Task<EndpointCallResult> CallBinaryAsync(
        EndpointCallRequest request, EndpointCallPlan plan, OutboundCall outbound, Guid endpointThingId,
        ObservedEdges observed, PassedOnRequestDeadline deadline)
    {
        if (outbound.Cached is { } fromDisk)
            return EndpointCallResult.Body(BinaryEnvelope(fromDisk.Bytes, fromDisk.ContentType), "application/json");

        // Bytes must be read before any string decode: ReadAsStringAsync replaces non-UTF-8
        // sequences with U+FFFD, which is lossy and irreversible. The envelope stays
        // application/json so the result flows through existing proxying unchanged.
        var (status, bytes, upstreamContentType, retryAfter) = await AskedOnceMoreWhileUnavailableAsync(
            plan.Unavailability, request.EndpointName!,
            token => SendAsync(plan, outbound.Headers, outbound.QueryParameters, request.Body, ReadBytes, token),
            answered => answered.StatusCode,
            answered => Encoding.UTF8.GetString(answered.Body),
            deadline.Token);

        var servedContentType = string.IsNullOrWhiteSpace(upstreamContentType) ? "application/octet-stream" : upstreamContentType;
        if (outbound.CacheKey != null && status is >= 200 and < 300)
            _diskCache.Write(request.EndpointName!, outbound.CacheKey, bytes, servedContentType);

        string Words() => Encoding.UTF8.GetString(bytes);
        var providerAway = plan.Unavailability.IsAnswered(status, Words);
        if (ProviderRefused(status) || providerAway)
            return EndpointCallResult.Refused(Words(), servedContentType, status, retryAfter, providerAway);

        if (plan.Keep != null && status is >= 200 and < 300
            && await KeepAsync(plan.Keep, bytes, servedContentType, endpointThingId, observed, deadline) is { } notKept)
            return notKept;

        return EndpointCallResult.Body(BinaryEnvelope(bytes, servedContentType), "application/json");
    }

    private async Task<EndpointCallResult> CallPagedAsync(
        EndpointCallRequest request, EndpointCallPlan plan, OutboundCall outbound, Guid endpointThingId,
        ObservedEdges observed, PassedOnRequestDeadline deadline)
    {
        var paging = plan.Paging!;
        string body;
        try
        {
            body = await OffsetPaginator.FetchAllPagesAsync(
                async (offset, ct) =>
                {
                    var pageParams = new Dictionary<string, string>(outbound.QueryParameters, StringComparer.OrdinalIgnoreCase)
                    {
                        [paging.OffsetParameter] = offset.ToString(CultureInfo.InvariantCulture)
                    };
                    if (paging.PageSizeParameter != null && paging.PageSize is > 0)
                        pageParams[paging.PageSizeParameter] = paging.PageSize.Value.ToString(CultureInfo.InvariantCulture);

                    var page = await SendAsync(plan, outbound.Headers, pageParams, request.Body, ReadText, ct);
                    if (page.StatusCode is < 200 or >= 300)
                        throw new PageNotAnsweredException(page.StatusCode, page.Body, page.ContentType, page.RetryAfter, offset);
                    return page.Body;
                },
                paging,
                deadline.Token);
        }
        catch (PageNotAnsweredException unanswered)
        {
            // The offset is about this walk, not the provider, so it goes to the log, not the answer.
            _logger.LogWarning(
                "Endpoint {EndpointName} was answered {Status} at offset {Offset}; the pages before it are dropped",
                request.EndpointName, unanswered.StatusCode, unanswered.Offset);
            return EndpointCallResult.Refused(
                unanswered.Body, unanswered.ContentType ?? "application/json", unanswered.StatusCode,
                unanswered.RetryAfter);
        }

        return await AnswerTextAsync(request, plan, endpointThingId, observed,
            new ProviderAnswer<string>(200, body, "application/json", null), deadline);
    }

    // Any page outside 2xx ends the walk, 404 included — which a single call takes for "holds
    // nothing about this subject" and a page cannot, because the aggregate can never be completed.
    private sealed class PageNotAnsweredException(
        int statusCode, string body, string? contentType, RetryConditionHeaderValue? retryAfter, int offset)
        : Exception($"The provider answered {statusCode} at offset {offset}.")
    {
        public int StatusCode { get; } = statusCode;
        public string Body { get; } = body;
        public string? ContentType { get; } = contentType;
        public RetryConditionHeaderValue? RetryAfter { get; } = retryAfter;
        public int Offset { get; } = offset;
    }

    private async Task<EndpointCallResult> CallOnceAsync(
        EndpointCallRequest request, EndpointCallPlan plan, OutboundCall outbound, Guid endpointThingId,
        ObservedEdges observed, PassedOnRequestDeadline deadline)
    {
        var answered = await AskedOnceMoreWhileUnavailableAsync(
            plan.Unavailability, request.EndpointName!,
            token => SendAsync(plan, outbound.Headers, outbound.QueryParameters, request.Body, ReadText, token),
            answer => answer.StatusCode,
            answer => answer.Body,
            deadline.Token);
        var (status, body, contentType, _) = answered;

        if (outbound.CacheKey != null && status is >= 200 and < 300)
            _diskCache.Write(request.EndpointName!, outbound.CacheKey, Encoding.UTF8.GetBytes(body), contentType ?? "application/json");

        if (plan.Keep != null && status is >= 200 and < 300
            && await KeepAsync(plan.Keep, Encoding.UTF8.GetBytes(body), contentType ?? "application/json",
                endpointThingId, observed, deadline) is { } notKept)
            return notKept;

        return await AnswerTextAsync(request, plan, endpointThingId, observed, answered, deadline);
    }

    private async Task<EndpointCallResult> AnswerTextAsync(
        EndpointCallRequest request,
        EndpointCallPlan plan,
        Guid endpointThingId,
        ObservedEdges observed,
        ProviderAnswer<string> answered,
        PassedOnRequestDeadline deadline)
    {
        var (status, body, contentType, retryAfter) = answered;
        var providerAway = plan.Unavailability.IsAnswered(status, () => body);
        if (ProviderRefused(status) || providerAway)
            return EndpointCallResult.Refused(body, contentType ?? "application/json", status, retryAfter, providerAway);

        if (plan.Reshape.Transform is { } transform && status is >= 200 and < 300)
        {
            deadline.Awaiting = AwaitedStep.ReadingsWrite;
            var ingestResult = await _observationService.CreateObservationsAsync(
                endpointThingId, transform, body, request.SubjectId, observed, deadline.Token);
            if (!ingestResult.Success)
                return new Refusal(400, ingestResult.Error ?? "Observation ingest failed.",
                    new Dictionary<string, object?> { ["detail"] = ingestResult.Detail }).ToResult();

            return EndpointCallResult.Ingested(new IngestSummary(
                endpointThingId, ingestResult.EntitiesTouched, ingestResult.ObservationsSubmitted,
                ingestResult.Written));
        }

        return EndpointCallResult.Body(body, contentType ?? "application/json");
    }

    // Deposit the bytes, then write the ticket the store answered onto the subject's property. Runs
    // only after a real 2xx fetch: a cache hit deposits nothing, because no new retrieval happened —
    // and the store's content addressing makes a re-deposit of the same bytes idempotent anyway.
    // The keep is a promise the model made, so any refused half — the deposit, the subject, the
    // ticket write — fails the call rather than answering as though something was kept: null means
    // kept, anything else is the 502 every other broker write failure here produces.
    private async Task<EndpointCallResult?> KeepAsync(
        KeepPlan keep, byte[] bytes, string contentType, Guid endpointThingId, ObservedEdges observed,
        PassedOnRequestDeadline deadline)
    {
        deadline.Awaiting = AwaitedStep.ResponseKept;
        var ticket = await _mycelium.DepositAssetAsync(bytes, contentType, deadline.Token);
        if (ticket == null)
            return Problem(502, "Asset keeping failed", "The asset store did not accept the response bytes.");

        var subject = await _mycelium.FindThingByNameAsync(keep.AssetSubject, deadline.Token);
        if (subject == null)
            return Problem(502, "Asset keeping failed", $"Asset subject thing not found: {keep.AssetSubject}");

        if (await _observationService.ObserveValueAsync(
                endpointThingId, subject.Value.Id, keep.AssetProperty, ticket, keep.ObservedAt, observed,
                deadline.Token) is { } failure)
            return Problem(502, "Asset keeping failed", failure);

        return null;
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

    private async Task<TAnswer> AskedOnceMoreWhileUnavailableAsync<TAnswer>(
        ProviderUnavailability unavailability,
        string endpointName,
        Func<CancellationToken, Task<TAnswer>> ask,
        Func<TAnswer, int> statusOf,
        Func<TAnswer, string> wordsOf,
        CancellationToken cancellationToken)
    {
        var first = await ask(cancellationToken);
        if (!unavailability.IsAnswered(statusOf(first), () => wordsOf(first)))
            return first;

        _logger.LogWarning(
            "Endpoint {EndpointName} was answered {Status} in the words of an unavailable provider; asking once more in {Seconds} seconds",
            endpointName, statusOf(first), unavailability.RetryAfter.TotalSeconds);
        await Task.Delay(unavailability.RetryAfter, _clock, cancellationToken);
        return await ask(cancellationToken);
    }

    private async Task<ProviderAnswer<TBody>> SendAsync<TBody>(
        EndpointCallPlan plan,
        IReadOnlyDictionary<string, string> headers,
        IReadOnlyDictionary<string, string> queryParameters,
        JsonElement body,
        Func<HttpContent, CancellationToken, Task<TBody>> readBody,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = plan.Timeout;

        using var request = OutboundRequest.Build(
            plan.Address.Method, plan.Address.Uri, body, headers, queryParameters, plan.RequestContentType, plan.AcceptHeader);

        using var response = await client.SendAsync(request, cancellationToken);
        var content = await readBody(response.Content, cancellationToken);
        var contentType = response.Content.Headers.ContentType?.ToString();
        return new ProviderAnswer<TBody>((int)response.StatusCode, content, contentType, response.Headers.RetryAfter);
    }

    // Text is read as text so it is decoded in the character set the provider declares, not assumed to be UTF-8.
    private static readonly Func<HttpContent, CancellationToken, Task<string>> ReadText =
        (content, cancellationToken) => content.ReadAsStringAsync(cancellationToken);

    private static readonly Func<HttpContent, CancellationToken, Task<byte[]>> ReadBytes =
        (content, cancellationToken) => content.ReadAsByteArrayAsync(cancellationToken);

    private sealed record ProviderAnswer<TBody>(
        int StatusCode, TBody Body, string? ContentType, RetryConditionHeaderValue? RetryAfter);
}
