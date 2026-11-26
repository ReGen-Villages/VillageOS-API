using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using vos.Service.Shared.Contracts.Validation;
using vos.Service.Shared.ServiceCalls;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Shared;

public abstract class MyceliumClientBase
{
    private const string MyceliumRegisterRequestSchemaId = "https://villageos/contracts/mycelium-register-request.schema.json";

    private static readonly Lazy<SchemaRegistry> _registry = new(() => new SchemaRegistry());
    private static readonly SchemaValidator _validator = new();

    protected readonly IHttpClientFactory HttpClientFactory;
    protected readonly ILogger Logger;
    public string MyceliumUrl { get; }
    private readonly Func<Task<string?>>? _tokenProvider;
    private readonly ServiceCredential _credential;

    public Guid HandlerId { get; } = Guid.NewGuid();

    // A debug build throws on a request its schema refuses, so a schema that has fallen behind is found;
    // a release build logs, so a stale schema never stops a live request.
    protected virtual SchemaViolationMode OutboundViolationMode =>
#if DEBUG
        SchemaViolationMode.Throw;
#else
        SchemaViolationMode.Log;
#endif

    protected void ValidateOutbound(string json, string schemaId)
    {
        var schema = _registry.Value.Get(schemaId);
        switch (OutboundViolationMode)
        {
            case SchemaViolationMode.Throw:
                _validator.ValidateOrThrow(json, schema, schemaId);
                break;
            case SchemaViolationMode.Log:
                _validator.ValidateForLog(json, schema, schemaId, Logger);
                break;
        }
    }

    protected MyceliumClientBase(
        IHttpClientFactory httpClientFactory, ILogger logger, string myceliumUrl, LaunchToken? launchToken = null,
        Func<Task<string?>>? tokenProvider = null, string? apiKey = null)
    {
        HttpClientFactory = httpClientFactory;
        Logger = logger;
        MyceliumUrl = myceliumUrl;
        _tokenProvider = tokenProvider;
        _credential = new ServiceCredential(httpClientFactory, logger, myceliumUrl, launchToken, apiKey);
    }

    private const string NoCredential = "neither ApiKey nor Token is set";

    // A token provider is a credential of the client's own, whatever the service was launched with.
    private bool HoldsACredential => _tokenProvider != null || _credential.Holds;

    // Null when the service holds no credential, and when the one it holds yields no token. The platform
    // refuses a token request that carries no key, so none is made.
    public async Task<string?> GetTokenAsync()
    {
        // Asked first: a subscription's own calls must never be re-pointed by the request a caller is inside.
        if (_tokenProvider != null)
            return await _tokenProvider();

        return await _credential.GetTokenAsync();
    }

    protected async Task<HttpClient> CreateAuthenticatedClientAsync(TimeSpan? timeout = null)
    {
        var token = await GetTokenAsync()
            ?? throw new InvalidOperationException(HoldsACredential
                ? "Failed to get authentication token"
                : $"{NoCredential}, so there is no credential to present to Mycelium");

        var client = HttpClientFactory.CreateClient();
        client.Timeout = timeout ?? TimeSpan.FromSeconds(5);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // Pauses on real time, never the model's clock, which a simulation runs faster.
    public async Task<bool> RegisterUntilSettledAsync(int port, string serviceName, string startCommand, string? logFilePrefix,
        string? logDirectory, TimeProvider time, CancellationToken cancellationToken)
    {
        for (var asks = 1; ; asks++)
        {
            var answer = await RegisterAsync(port, serviceName, startCommand, logFilePrefix, logDirectory);
            if (answer.Outcome != RegistrationOutcome.AskAgain)
            {
                if (asks > 1 && answer.Outcome == RegistrationOutcome.Accepted)
                    Logger.LogInformation("Mycelium took the registration at ask {Asks}", asks);
                return answer.Outcome == RegistrationOutcome.Accepted;
            }

            if (asks == 1)
                Logger.LogWarning("Mycelium has not taken the registration ({Why}); it is asked again, each pause longer, until it does",
                    answer.Why);
            await Task.Delay(AskingAgain.PauseAfter(asks, answer.RetryAfter), time, cancellationToken);
        }
    }

    public async Task<RegistrationAnswer> RegisterAsync(int port, string serviceName, string startCommand, string? logFilePrefix = null, string? logDirectory = null)
    {
        var registration = new Dictionary<string, object>
        {
            ["handlerId"] = HandlerId,
            ["serviceName"] = serviceName,
            ["endpointUrl"] = $"http://localhost:{port}",
            ["startCommand"] = startCommand,
            ["stopEndpoint"] = $"http://localhost:{port}/shutdown",
            ["healthEndpoint"] = $"http://localhost:{port}/health"
        };
        if (logFilePrefix is not null) registration["logFilePrefix"] = logFilePrefix;
        if (logDirectory is not null) registration["logDirectory"] = logDirectory;

        var json = JsonSerializer.Serialize(registration);

        // Outside the network try/catch so schema-violation exceptions are not swallowed.
        ValidateOutbound(json, MyceliumRegisterRequestSchemaId);

        if (!HoldsACredential)
        {
            Logger.LogWarning("{Reason}, so this service has not registered with mycelium", NoCredential);
            return RegistrationAnswer.Refused;
        }

        try
        {
            var client = await CreateAuthenticatedClientAsync();
            using var response = await client.PostAsync(
                $"{MyceliumUrl}/api/mycelium/register",
                new StringContent(json, Encoding.UTF8, "application/json"));

            var answer = RegistrationAnswer.To(response);
            if (answer.Outcome == RegistrationOutcome.Accepted)
                Logger.LogInformation("Registered with mycelium as {HandlerId}", HandlerId);
            else if (answer.Outcome == RegistrationOutcome.Refused)
                Logger.LogError("Mycelium refused the registration and would refuse it again: {StatusCode}", response.StatusCode);
            return answer;
        }
        catch (InvalidOperationException)
        {
            return RegistrationAnswer.AskAgainBecause("no token could be had");
        }
        catch (Exception exception)
        {
            return RegistrationAnswer.AskAgainBecause(exception.Message);
        }
    }

    // Write kinds — Facts, Observations, Sediment. Each validates its payload against an embedded
    // schema (Contracts/Schemas), then throws an HttpRequestException carrying the StatusCode on failure.

    private const string FactWriteRequestSchemaId = "https://villageos/contracts/fact-write-request.schema.json";
    private const string ObservationWriteRequestSchemaId = "https://villageos/contracts/observation-write-request.schema.json";
    private const string ObservationBatchRequestSchemaId = "https://villageos/contracts/observation-batch-request.schema.json";
    private const string SedimentDepositRequestSchemaId = "https://villageos/contracts/sediment-deposit-request.schema.json";

    // A cancellation the caller asked for is not a fault, so it is not logged as one.
    protected async Task<bool> PostRelationshipAsync(
        Guid subjectId, Guid predicateId, Guid targetId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync(timeout);
            var response = await client.PostAsJsonAsync(
                $"{MyceliumUrl}/api/relationships", new { subjectId, predicateId, targetId }, cancellationToken);
            if (response.IsSuccessStatusCode)
                return true;

            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            Logger.LogWarning(
                "Failed to create relationship {Subject} -[{Predicate}]-> {Target}. Status: {Status}. Error: {Error}",
                subjectId, predicateId, targetId, response.StatusCode, error);
            return false;
        }
        catch (Exception exception)
        {
            if (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
                Logger.LogError(exception, "Error creating relationship {Subject} -[{Predicate}]-> {Target}",
                    subjectId, predicateId, targetId);
            return false;
        }
    }

    protected Task<PlatformAnswer> AskForThingNamedAsync(string name, TimeSpan? timeout, CancellationToken cancellationToken) =>
        AskAsync(HttpMethod.Get, $"/api/things?name={Uri.EscapeDataString(name)}", null, timeout, cancellationToken);

    protected Task<PlatformAnswer> AskToCreateThingAsync(HttpContent thing, TimeSpan? timeout, CancellationToken cancellationToken) =>
        AskAsync(HttpMethod.Post, "/api/things", thing, timeout, cancellationToken);

    protected Task<PlatformAnswer> AskToDeleteThingAsync(Guid thingId, TimeSpan? timeout, CancellationToken cancellationToken) =>
        AskAsync(HttpMethod.Delete, $"/api/things/{thingId}", null, timeout, cancellationToken);

    protected Task<PlatformAnswer> AskToApplyFragmentAsync(HttpContent fragment, TimeSpan? timeout, CancellationToken cancellationToken) =>
        AskAsync(HttpMethod.Post, "/api/model/fragment", fragment, timeout, cancellationToken);

    // On a loaded build agent one relationship write took fifteen seconds, and a shorter wait reported a failure while
    // the write went on to land.
    public static readonly TimeSpan BrokerPatience = TimeSpan.FromSeconds(30);

    // The broker refuses a request above its body limit, and one slice of every climate property comes close to it, so a
    // slice goes in several requests.
    public const int ReadingsPerDeposit = 50_000;

    // The broker validates a request whole before storing any of it and drops a reading it already holds with the same
    // value, so a deposit that stopped at a refused request can be sent again from the start.
    public async Task<BrokerAnswer> DepositReadingsAsync(
        Guid subjectId, IReadOnlyList<ObservationSample> readings, CancellationToken cancellationToken = default)
    {
        var untimed = readings.Where(reading => reading.ObservedAt is null).Select(reading => reading.Property).Distinct().ToList();
        if (untimed.Count > 0)
            return new BrokerAnswer(false, $"The bulk route takes only readings with their time, and {string.Join(", ", untimed)} carries none.");

        foreach (var request in DepositRequests.Of(readings, ReadingsPerDeposit))
        {
            var answer = await PostAsync("/api/sediment", request.Select(reading => new
            {
                objectId = subjectId,
                property = reading.Property,
                value = ResolveObservationValue(reading.Value),
                observedAt = reading.ObservedAt,
            }), cancellationToken);
            if (!answer.Accepted) return answer;
        }
        return BrokerAnswer.Done;
    }

    public Task<BrokerAnswer> ReportHistoryChangedAsync(Guid thingId, CancellationToken cancellationToken = default) =>
        PostAsync($"/api/things/{thingId}/history/changed", body: null, cancellationToken);

    // A reshaped reading carries whatever the source's JSON held. Unwrap the element to the scalar
    // the observation route stores; a shape it has no scalar for keeps its own JSON text.
    protected static object? ResolveObservationValue(object? value) =>
        value is not JsonElement element ? value
        : JsonScalar.TryRead(element, out var scalar) ? scalar
        : element.GetRawText();

    private async Task<BrokerAnswer> PostAsync(string path, object? body, CancellationToken cancellationToken)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync(BrokerPatience);
            using var response = body is null
                ? await client.PostAsync($"{MyceliumUrl}{path}", content: null, cancellationToken)
                : await client.PostAsJsonAsync($"{MyceliumUrl}{path}", body, cancellationToken);
            if (response.IsSuccessStatusCode) return BrokerAnswer.Done;

            var answerBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var refusal = $"{(int)response.StatusCode}: {PlatformAnswer.ErrorWordsIn(answerBody) ?? answerBody}";
            Logger.LogWarning("The broker refused {Path}: {Refusal}", path, refusal);
            return new BrokerAnswer(false, refusal, CanBeTriedAgain: (int)response.StatusCode >= 500, response.Headers.RetryAfter?.Delta);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "The broker could not be reached for {Path}", path);
            return new BrokerAnswer(false, $"The broker could not be reached: {exception.Message}", CanBeTriedAgain: true);
        }
    }

    private async Task<PlatformAnswer> AskAsync(
        HttpMethod method, string path, HttpContent? content, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var client = await CreateAuthenticatedClientAsync(timeout);
        var request = new HttpRequestMessage(method, $"{MyceliumUrl}{path}") { Content = content };
        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var outcome = response.IsSuccessStatusCode ? PlatformOutcome.Done
            : response.StatusCode == HttpStatusCode.NotFound ? PlatformOutcome.NotFound
            : PlatformOutcome.Refused;
        return new PlatformAnswer(outcome, response.StatusCode, body);
    }

    protected static Guid? IdentifierOf(JsonElement thing) =>
        TryGetPropertyCaseInsensitive(thing, "Id", out var identifier)
        && identifier.ValueKind == JsonValueKind.String
        && Guid.TryParse(identifier.GetString(), out var parsed)
            ? parsed
            : null;

    protected static bool TryReadThing(JsonElement thing, out Guid identifier, out string name)
    {
        name = string.Empty;
        identifier = IdentifierOf(thing) ?? Guid.Empty;
        if (identifier == Guid.Empty
            || !TryGetPropertyCaseInsensitive(thing, "Name", out var named)
            || named.ValueKind != JsonValueKind.String)
            return false;

        name = named.GetString() ?? string.Empty;
        return true;
    }

    // A structural Fact, applied at once and never dropped. Returns the commit sequence number; throws
    // with StatusCode on failure (405 where the property takes only Observations, 404 for an unknown Thing).
    public async Task<long> SetFactAsync(Guid thingId, string property, object? value)
    {
        var json = JsonSerializer.Serialize(new { value });
        ValidateOutbound(json, FactWriteRequestSchemaId);

        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(30));
        var response = await client.PostAsync(
            $"{MyceliumUrl}{MyceliumRoutes.ThingPropertyFacts(thingId, property)}",
            new StringContent(json, Encoding.UTF8, "application/json"));

        if (response.StatusCode == HttpStatusCode.Created)
        {
            var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
            return doc.TryGetProperty("sequenceNumber", out var seq) ? seq.GetInt64() : 0L;
        }

        throw await FailureAsync("Fact write", response);
    }

    public async Task ReportCallAsync(Guid callRecordId, CallReport report, CancellationToken cancellationToken = default)
    {
        var json = report.Reason is { } reason
            ? JsonSerializer.Serialize(new { report = report.Word, reason })
            : JsonSerializer.Serialize(new { report = report.Word });

        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(15));
        var response = await client.PostAsync(
            $"{MyceliumUrl}{MyceliumRoutes.CallReport(callRecordId)}",
            new StringContent(json, Encoding.UTF8, "application/json"), cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new CallRecordNoLongerWaitingException(callRecordId, report, await response.Content.ReadAsStringAsync(cancellationToken));
        if (!response.IsSuccessStatusCode)
            throw await FailureAsync("Call report", response);
    }

    // One Observation, queued and batched (202). Pass observedAt for a late or out-of-order sample.
    // Throws on failure (405 where the property takes only Facts).
    public async Task RecordObservationAsync(Guid thingId, string property, object? value, DateTime? observedAt = null)
    {
        var json = observedAt is { } at
            ? JsonSerializer.Serialize(new { value, observedAt = at })
            : JsonSerializer.Serialize(new { value });
        ValidateOutbound(json, ObservationWriteRequestSchemaId);

        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(30));
        var response = await client.PostAsync(
            $"{MyceliumUrl}/api/things/{thingId}/properties/{Uri.EscapeDataString(property)}/observations",
            new StringContent(json, Encoding.UTF8, "application/json"));

        if (!response.IsSuccessStatusCode)
            throw await FailureAsync("Observation write", response);
    }

    // Record many Observations across an entity's properties in one batch (202); returns the accepted count.
    public async Task<int> RecordObservationsAsync(Guid thingId, IReadOnlyList<ObservationSample> samples)
    {
        if (samples.Count == 0) return 0;

        var json = JsonSerializer.Serialize(samples.Select(s => s.ObservedAt is { } at
            ? (object)new { property = s.Property, value = s.Value, observedAt = at }
            : new { property = s.Property, value = s.Value }));
        ValidateOutbound(json, ObservationBatchRequestSchemaId);

        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(30));
        var response = await client.PostAsync(
            $"{MyceliumUrl}/api/things/{thingId}/observations",
            new StringContent(json, Encoding.UTF8, "application/json"));

        if (!response.IsSuccessStatusCode)
            throw await FailureAsync("Observation batch", response);

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        return doc.TryGetProperty("accepted", out var n) ? n.GetInt32() : samples.Count;
    }

    // Deposits historical readings into sealed Sapwood (202). Each reading's Thing must exist and every
    // reading needs an ObservedAt. Returns the deposit summary.
    public async Task<SedimentDepositResult> DepositSedimentAsync(IReadOnlyList<SedimentReading> readings)
    {
        if (readings.Count == 0)
            throw new ArgumentException("At least one reading is required.", nameof(readings));

        var json = JsonSerializer.Serialize(readings.Select(r => new
        {
            objectId = r.ObjectId,
            property = r.Property,
            value = r.Value,
            observedAt = r.ObservedAt
        }));
        ValidateOutbound(json, SedimentDepositRequestSchemaId);

        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(60));
        var response = await client.PostAsync(
            $"{MyceliumUrl}/api/sediment",
            new StringContent(json, Encoding.UTF8, "application/json"));

        if (!response.IsSuccessStatusCode)
            throw await FailureAsync("Sediment deposit", response);

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new SedimentDepositResult(
            doc.TryGetProperty("batchId", out var b) && b.TryGetGuid(out var bid) ? bid : Guid.Empty,
            doc.TryGetProperty("series", out var s) ? s.GetInt32() : 0,
            doc.TryGetProperty("buckets", out var bk) ? bk.GetInt32() : 0,
            doc.TryGetProperty("samples", out var sm) ? sm.GetInt64() : 0L);
    }

    // What time the model thinks it is, and how fast that runs against real time — null where the
    // broker could not be asked. A service stamping a model value reads this rather than its own wall
    // clock, so an instant it writes and one the platform writes come off the same clock.
    public async Task<ModelTimeReading?> ReadModelTimeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync();
            var response = await client.GetAsync($"{MyceliumUrl}/api/time", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Logger.LogDebug("Reading the model clock answered {Status}", (int)response.StatusCode);
                return null;
            }

            return ParseModelTime(await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Judged by the caller's token, not the exception's type: the client's own timeout throws an
            // OperationCanceledException too, and that one is a broker too slow to answer, not a caller stopping.
            // Said at debug, and by the caller once rather than once per reconnect: a broker that is
            // away for an hour would otherwise write the same warning several hundred times.
            Logger.LogDebug(exception, "Could not read the model clock");
            return null;
        }
    }

    public const string ModelClockChangedEvent = "ModelClockChanged";

    // The model clock as the broker tells it: a reading taken when the system-events stream opens, then
    // one for each change the broker announces, until the stream ends. Yields nothing where the broker
    // could not be reached; the caller reconnects.
    public async IAsyncEnumerable<ModelTimeReading> ListenForModelClockAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var events = await OpenSystemEventsAsync(cancellationToken);
        if (events is null) yield break;

        using (events)
        {
            if (await ReadModelTimeAsync(cancellationToken) is { } reading)
                yield return reading;

            await foreach (var frame in SseEventReader.ReadAsync(await events.Content.ReadAsStreamAsync(cancellationToken), cancellationToken))
            {
                if (frame.EventType != ModelClockChangedEvent) continue;
                if (ParseModelTime(frame.Data) is { } announced) yield return announced;
            }
        }
    }

    private async Task<HttpResponseMessage?> OpenSystemEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync(Timeout.InfiniteTimeSpan);
            var request = new HttpRequestMessage(HttpMethod.Get, $"{MyceliumUrl}/api/events/stream");
            request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode) return response;

            Logger.LogDebug("Opening the system-events stream answered {Status}", (int)response.StatusCode);
            response.Dispose();
            return null;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.LogDebug(exception, "Could not open the system-events stream");
            return null;
        }
    }

    private static ModelTimeReading? ParseModelTime(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return ParseModelTime(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ModelTimeReading? ParseModelTime(JsonElement answer)
    {
        if (!TryGetPropertyCaseInsensitive(answer, "now", out var now)
            || !now.TryGetDateTimeOffset(out var instant))
            return null;

        var rate = TryGetPropertyCaseInsensitive(answer, "rate", out var stated)
                   && stated.TryGetDouble(out var read) ? read : 1.0;
        return new ModelTimeReading(instant, rate);
    }

    private static async Task<HttpRequestException> FailureAsync(string op, HttpResponseMessage response)
    {
        var error = await response.Content.ReadAsStringAsync();
        return new HttpRequestException(
            $"{op} failed ({(int)response.StatusCode} {response.StatusCode}): {error}", null, response.StatusCode);
    }

    // A Thing the broker answers with is serialized under the naming policy it shares with the
    // seed files — Id, Name — while the payloads it accepts and most of the rest of the
    // platform's JSON read camelCase. A client that asks for the wrong casing reads the field as absent
    // and the whole call as failed, so every client reads an answer through this — including one that
    // reads a snapshot as the broker wrote it rather than through a typed record, which is why this is
    // reachable from outside the clients themselves.
    // An answer that is not an object at all is no field either, rather than the
    // InvalidOperationException asking a non-object for a property would throw.
    public static bool TryGetPropertyCaseInsensitive(
        JsonElement element, string propertyName, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        if (element.TryGetProperty(propertyName, out value))
            return true;

        foreach (var property in element.EnumerateObject())
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }

        value = default;
        return false;
    }
}
