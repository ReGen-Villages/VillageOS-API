using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using vos.Service.Shared.Contracts.Validation;

namespace vos.Service.Shared;

public abstract class MyceliumClientBase
{
    private const string MyceliumRegisterRequestSchemaId = "https://villageos/contracts/mycelium-register-request.schema.json";
    private const string TokenResponseSchemaId = "https://villageos/contracts/token-response.schema.json";

    private static readonly Lazy<SchemaRegistry> _registry = new(() => new SchemaRegistry());
    private static readonly SchemaValidator _validator = new();

    protected readonly IHttpClientFactory HttpClientFactory;
    protected readonly ILogger Logger;
    public string MyceliumUrl { get; }
    private readonly Func<Task<string?>>? _tokenProvider;
    private readonly ServiceCredential _credential;

    public Guid HandlerId { get; } = Guid.NewGuid();

    // Failure policy for outbound contract violations: Debug throws to surface schema drift;
    // Release logs so a stale schema never blocks production traffic.
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
        IHttpClientFactory httpClientFactory, ILogger logger, string myceliumUrl, string? serviceToken = null,
        Func<Task<string?>>? tokenProvider = null, string? apiKey = null)
    {
        HttpClientFactory = httpClientFactory;
        Logger = logger;
        MyceliumUrl = myceliumUrl;
        _tokenProvider = tokenProvider;
        _credential = new ServiceCredential(httpClientFactory, logger, myceliumUrl, serviceToken, apiKey);
    }

    public async Task<string?> GetTokenAsync()
    {
        // A client built with a provider speaks for one model whatever else is in hand, so it is asked
        // first: a subscription's own calls must never be re-pointed by the request a caller is inside.
        if (_tokenProvider != null)
            return await _tokenProvider();

        // Whatever the service holds is final, including a key whose exchange came back with nothing:
        // asking the broker below would reach further than the credential the operator chose.
        if (_credential.Holds)
            return await _credential.GetTokenAsync();

        string body;
        try
        {
            var client = HttpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var response = await client.PostAsync($"{MyceliumUrl}/api/auth/token", null);
            response.EnsureSuccessStatusCode();

            body = await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to get token from mycelium");
            return null;
        }

        // Outside the network try/catch so contract violations are not swallowed.
        ValidateOutbound(body, TokenResponseSchemaId);

        try
        {
            var result = JsonSerializer.Deserialize<JsonElement>(body);
            return result.GetProperty("token").GetString();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to parse token from mycelium response");
            return null;
        }
    }

    protected async Task<HttpClient> CreateAuthenticatedClientAsync(TimeSpan? timeout = null)
    {
        var token = await GetTokenAsync()
            ?? throw new InvalidOperationException("Failed to get authentication token");

        var client = HttpClientFactory.CreateClient();
        client.Timeout = timeout ?? TimeSpan.FromSeconds(5);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<bool> RegisterAsync(int port, string serviceName, string startCommand)
    {
        var registration = new
        {
            handlerId = HandlerId,
            serviceName,
            endpointUrl = $"http://localhost:{port}",
            startCommand,
            stopEndpoint = $"http://localhost:{port}/shutdown",
            healthEndpoint = $"http://localhost:{port}/health"
        };

        var json = JsonSerializer.Serialize(registration);

        // Outside the network try/catch so schema-violation exceptions are not swallowed.
        ValidateOutbound(json, MyceliumRegisterRequestSchemaId);

        try
        {
            var client = await CreateAuthenticatedClientAsync();
            var response = await client.PostAsync(
                $"{MyceliumUrl}/api/mycelium/register",
                new StringContent(json, Encoding.UTF8, "application/json"));

            if (response.IsSuccessStatusCode)
            {
                Logger.LogInformation("Registered with mycelium as {HandlerId}", HandlerId);
                return true;
            }

            Logger.LogError("Registration failed: {StatusCode}", response.StatusCode);
            return false;
        }
        catch (InvalidOperationException)
        {
            Logger.LogError("Cannot register: failed to get token");
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error registering with mycelium");
            return false;
        }
    }

    // Write kinds — Facts, Observations, Sediment. Each validates its payload against an embedded
    // schema (Contracts/Schemas), then throws an HttpRequestException carrying the StatusCode on failure.

    private const string FactWriteRequestSchemaId = "https://villageos/contracts/fact-write-request.schema.json";
    private const string ObservationWriteRequestSchemaId = "https://villageos/contracts/observation-write-request.schema.json";
    private const string ObservationBatchRequestSchemaId = "https://villageos/contracts/observation-batch-request.schema.json";
    private const string SedimentDepositRequestSchemaId = "https://villageos/contracts/sediment-deposit-request.schema.json";

    // Assert a structural Fact (synchronous, never lossy). Returns the commit sequence
    // number; throws with StatusCode on failure (405 ObservationOnly, 404 unknown).
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

    // Record one Observation (queued/batched, 202). Pass observedAt for
    // late/out-of-order samples. Throws on failure (405 FactOnly).
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

    // Bulk-deposit historical readings to sealed Sapwood (202). Entities must exist and every
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

    private static async Task<HttpRequestException> FailureAsync(string op, HttpResponseMessage response)
    {
        var error = await response.Content.ReadAsStringAsync();
        return new HttpRequestException(
            $"{op} failed ({(int)response.StatusCode} {response.StatusCode}): {error}", null, response.StatusCode);
    }

    /// <summary>A Thing the broker answers with is serialized under the naming policy it shares with the
    /// seed files — <c>Id</c>, <c>Name</c> — while the payloads it accepts and most of the rest of the
    /// platform's JSON read camelCase. A client that asks for the wrong casing reads the field as absent
    /// and the whole call as failed, so every client reads an answer through this.</summary>
    /// <remarks>An answer that is not an object at all is no field either, rather than the
    /// <see cref="InvalidOperationException"/> asking a non-object for a property would throw.</remarks>
    protected static bool TryGetPropertyCaseInsensitive(
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
