using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using vos.ManagedMicroservice.Shared.Contracts.Validation;

namespace vos.ManagedMicroservice.Shared;

public abstract class MyceliumClientBase
{
    private const string MyceliumRegisterRequestSchemaId = "https://villageos/contracts/mycelium-register-request.schema.json";
    private const string TokenResponseSchemaId = "https://villageos/contracts/token-response.schema.json";

    private static readonly Lazy<SchemaRegistry> _registry = new(() => new SchemaRegistry());
    private static readonly SchemaValidator _validator = new();

    protected readonly IHttpClientFactory HttpClientFactory;
    protected readonly ILogger Logger;
    public string MyceliumUrl { get; }
    private readonly string? _serviceToken;

    public Guid HandlerId { get; } = Guid.NewGuid();

    /// <summary>
    /// Failure policy for outbound contract violations: Debug throws to surface schema drift;
    /// Release logs so a stale schema never blocks production traffic.
    /// </summary>
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

    protected MyceliumClientBase(IHttpClientFactory httpClientFactory, ILogger logger, string myceliumUrl, string? serviceToken = null)
    {
        HttpClientFactory = httpClientFactory;
        Logger = logger;
        MyceliumUrl = myceliumUrl;
        _serviceToken = serviceToken;
    }

    public async Task<string?> GetTokenAsync()
    {
        if (!string.IsNullOrEmpty(_serviceToken))
            return _serviceToken;

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

    public virtual async Task DeregisterAsync()
    {
        try
        {
            var token = await GetTokenAsync();
            if (token == null)
            {
                Logger.LogWarning("Cannot deregister: failed to get token");
                return;
            }

            var client = HttpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.DeleteAsync($"{MyceliumUrl}/api/mycelium/services/{HandlerId}");

            if (response.IsSuccessStatusCode)
                Logger.LogInformation("Deregistered from mycelium");
            else
                Logger.LogWarning("Deregistration returned: {StatusCode}", response.StatusCode);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deregistering from mycelium");
        }
    }

    // ===================== Write kinds: Facts · Observations · Sediment =====================
    // Reference helpers every microservice can use to write back to the model. Each validates its
    // outbound payload against an embedded contract schema (Contracts/Schemas) before the network
    // call — exactly as RegisterAsync does — then surfaces failures (404, 405, …) as an
    // HttpRequestException carrying the StatusCode. See docs/MICROSERVICE_CONTRACT.md.

    private const string FactWriteRequestSchemaId = "https://villageos/contracts/fact-write-request.schema.json";
    private const string ObservationWriteRequestSchemaId = "https://villageos/contracts/observation-write-request.schema.json";
    private const string ObservationBatchRequestSchemaId = "https://villageos/contracts/observation-batch-request.schema.json";
    private const string SedimentDepositRequestSchemaId = "https://villageos/contracts/sediment-deposit-request.schema.json";

    /// <summary>
    /// Assert a structural <b>Fact</b> — the synchronous, never-lossy write kind. Use it for truth
    /// that must survive replay (a status, a configuration value, a corrected reading). Returns the
    /// commit sequence number Mycelium assigned. Throws <see cref="HttpRequestException"/> (with
    /// <see cref="HttpRequestException.StatusCode"/>) on failure — e.g. 405 when the property is
    /// ObservationOnly, 404 when the thing/property is unknown.
    /// </summary>
    public async Task<long> SetFactAsync(Guid thingId, string property, object? value)
    {
        var json = JsonSerializer.Serialize(new { value });
        ValidateOutbound(json, FactWriteRequestSchemaId);

        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(30));
        var response = await client.PostAsync(
            $"{MyceliumUrl}/api/things/{thingId}/properties/{Uri.EscapeDataString(property)}/facts",
            new StringContent(json, Encoding.UTF8, "application/json"));

        if (response.StatusCode == HttpStatusCode.Created)
        {
            var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
            return doc.TryGetProperty("sequenceNumber", out var seq) ? seq.GetInt64() : 0L;
        }

        throw await FailureAsync("Fact write", response);
    }

    /// <summary>
    /// Record a single <b>Observation</b> — sampled telemetry, queued and batched by Mycelium
    /// (202 Accepted). Pass <paramref name="observedAt"/> for late / out-of-order samples; omit it
    /// to let Mycelium stamp now. Throws on failure — e.g. 405 when the property is FactOnly.
    /// </summary>
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

    /// <summary>
    /// Record many <b>Observations</b> across one entity's properties in a single batch (202).
    /// Returns the accepted-sample count Mycelium reports.
    /// </summary>
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

    /// <summary>
    /// Deposit a batch of historical readings as <b>Sediment</b> — written straight to sealed Sapwood
    /// buckets, bypassing the live observation queue (202 Accepted). Every entity must already exist
    /// and every reading must carry an <c>ObservedAt</c>. Returns the deposit summary
    /// (<c>batchId</c>, <c>series</c>, <c>buckets</c>, <c>samples</c>).
    /// </summary>
    public async Task<SedimentDepositResult> DepositSedimentAsync(IReadOnlyList<SedimentReading> readings)
    {
        if (readings.Count == 0)
            throw new ArgumentException("At least one reading is required.", nameof(readings));

        var json = JsonSerializer.Serialize(readings.Select(r => new
        {
            thingId = r.ThingId,
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
}
