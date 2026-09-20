using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

[assembly: InternalsVisibleTo("vos.Taproot.Tests")]

namespace vos.Taproot;

public readonly record struct LogDownload(string FileName, Stream Content);

public class MyceliumClient
{
    private readonly HttpClient _httpClient;
    private readonly HttpClient _streamClient;
    private readonly string _myceliumUrl;
    private readonly string? _apiKey;
    private readonly Func<DateTime> _clock;
    private string? _cachedToken;
    private DateTime _tokenExpiry;

    public MyceliumClient(string myceliumUrl) : this(myceliumUrl, null) { }

    public MyceliumClient(string myceliumUrl, string? apiKey)
    {
        _myceliumUrl = myceliumUrl.TrimEnd('/');
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("VOS_API_KEY");
        _clock = () => DateTime.UtcNow;

        _httpClient = new HttpClient(CreateHandler())
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _streamClient = new HttpClient(CreateHandler())
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    // TLS certificates are validated by default. Validation is bypassed only when VOS_INSECURE_TLS
    // is explicitly set — for local dev against self-signed certs, never in production.
    internal static HttpClientHandler CreateHandler()
    {
        var handler = new HttpClientHandler();
        if (IsInsecureTlsEnabled())
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        return handler;
    }

    internal static bool IsInsecureTlsEnabled()
        => Environment.GetEnvironmentVariable("VOS_INSECURE_TLS") is "1" or "true" or "TRUE" or "True";

    // Test seam: lets tests inject a mock handler and a virtual clock.
    internal MyceliumClient(string myceliumUrl, string? apiKey, HttpClient httpClient, Func<DateTime>? clock = null)
    {
        _myceliumUrl = myceliumUrl.TrimEnd('/');
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("VOS_API_KEY");
        _httpClient = httpClient;
        _streamClient = httpClient;
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    public virtual async Task<string> GetTokenAsync()
    {
        if (_cachedToken != null && _clock() < _tokenExpiry)
        {
            return _cachedToken;
        }

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException(
                "No API key configured. Set the VOS_API_KEY environment variable before starting the CLI.");
        }

        var request = new HttpRequestMessage(HttpMethod.Post, $"{_myceliumUrl}/api/auth/token");
        request.Headers.Add("X-API-Key", _apiKey);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"API key authentication failed ({response.StatusCode}): {body}");
        }

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        _cachedToken = result.GetProperty("token").GetString()!;
        _tokenExpiry = _clock().AddMinutes(4); // API key JWTs are 5min, refresh early
        return _cachedToken;
    }

    private async Task SetAuthHeaderAsync()
    {
        var token = await GetTokenAsync();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public virtual async Task<JsonElement> GetLogTailAsync(int? lines, string? service)
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync(
            $"{_myceliumUrl}/api/logs/tail{Query(("lines", lines?.ToString()), ("service", service))}");
        await EnsureSuccessCarryingTheReasonAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<LogDownload> DownloadLogAsync(string? service)
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync(
            $"{_myceliumUrl}/api/logs/download{Query(("service", service))}", HttpCompletionOption.ResponseHeadersRead);
        await EnsureSuccessCarryingTheReasonAsync(response);
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
            ?? (service is null ? "mycelium.log" : $"{service}.log");
        return new LogDownload(fileName, await response.Content.ReadAsStreamAsync());
    }

    public virtual IAsyncEnumerable<ServerSentEvent> FollowLogAsync(int? tail, string? service, CancellationToken cancellationToken)
        => StreamAsync($"{_myceliumUrl}/api/logs/stream{Query(("tail", tail?.ToString()), ("service", service))}", cancellationToken);

    public virtual IAsyncEnumerable<ServerSentEvent> WatchEventsAsync(CancellationToken cancellationToken)
        => StreamAsync($"{_myceliumUrl}/api/events/stream", cancellationToken);

    // A stream stays open as long as the caller listens, so it cannot go through the client whose
    // timeout bounds every request.
    private async IAsyncEnumerable<ServerSentEvent> StreamAsync(
        string url, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _streamClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessCarryingTheReasonAsync(response);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(body);
        await foreach (var received in ServerSentEvents.ReadAsync(reader, cancellationToken))
            yield return received;
    }

    private static string Query(params (string Name, string? Value)[] parameters)
    {
        var present = parameters
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Value))
            .Select(parameter => $"{parameter.Name}={Uri.EscapeDataString(parameter.Value!)}")
            .ToList();
        return present.Count > 0 ? "?" + string.Join("&", present) : "";
    }

    private static string BuildTimeRangeQuery(DateTime? startTime, DateTime? endTime)
        => Query(("startTime", startTime?.ToString("O")), ("endTime", endTime?.ToString("O")));

    public virtual async Task<JsonElement> GetAllThingsAsync()
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync($"{_myceliumUrl}/api/things");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // The platform answers one name with the Thing itself and 404 with none, several names with a list;
    // a narrowed list is a list either way to its caller.
    public virtual async Task<JsonElement> GetThingsAsync(ThingListNarrowing narrowing)
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync($"{_myceliumUrl}/api/things" + Query(
            ("name", narrowing.Names),
            ("type", narrowing.Type),
            ("within", narrowing.Within?.ToString()),
            ("limit", narrowing.Limit?.ToString()),
            ("properties", narrowing.Properties)));
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return JsonDocument.Parse("[]").RootElement;
        await EnsureSuccessCarryingTheReasonAsync(response);
        var answer = await response.Content.ReadFromJsonAsync<JsonElement>();
        return answer.ValueKind == JsonValueKind.Object
            ? JsonDocument.Parse($"[{answer.GetRawText()}]").RootElement
            : answer;
    }

    // Every Thing's resolved properties in one call, keyed by Thing id. scope: effective (own +
    // inherited with own/overrides winning) | own | inherited. Each property carries IsInherited /
    // InheritedFrom; inherited ones are keyed by qualified path ("Device.serialNumber"). Lets snapshot
    // read commands surface the full inherited view without a per-Thing request.
    public virtual async Task<JsonElement> GetAllPropertiesAsync(string scope = "effective")
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync($"{_myceliumUrl}/api/things/properties?scope={scope}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement?> GetThingAsync(Guid id)
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync($"{_myceliumUrl}/api/things/{id}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement> CreateThingAsync(string name)
    {
        await SetAuthHeaderAsync();
        var content = new StringContent(
            JsonSerializer.Serialize(new { Name = name }),
            Encoding.UTF8,
            "application/json");
        var response = await _httpClient.PostAsync($"{_myceliumUrl}/api/things", content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<bool> DeleteThingAsync(Guid id)
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.DeleteAsync($"{_myceliumUrl}/api/things/{id}");
        return response.IsSuccessStatusCode;
    }

    // Rename a Thing in place — keeps its Id and all edges (unlike delete+recreate). The broker
    // persists a NameSet Fact, so the rename streams over SSE and is temporally reconstructable.
    public virtual async Task<bool> RenameThingAsync(Guid id, string newName)
    {
        await SetAuthHeaderAsync();
        var content = new StringContent(
            JsonSerializer.Serialize(new { Name = newName }),
            Encoding.UTF8,
            "application/json");
        var response = await _httpClient.PutAsync($"{_myceliumUrl}/api/things/{id}/name", content);
        return response.IsSuccessStatusCode;
    }

    // The platform changes a property a Thing already holds through PUT and adds one through POST on
    // the same path; a set sent where an add was meant answers 404.
    public virtual Task<JsonElement> SetPropertyAsync(Guid thingId, string name, string type, object? value)
        => WritePropertyAsync(HttpMethod.Put, $"{_myceliumUrl}/api/things/{thingId}/properties", name, type, value);

    public virtual Task<JsonElement> AddPropertyAsync(Guid thingId, string name, string type, object? value)
        => WritePropertyAsync(HttpMethod.Post, $"{_myceliumUrl}/api/things/{thingId}/properties", name, type, value);

    private async Task<JsonElement> WritePropertyAsync(HttpMethod method, string url, string name, string type, object? value)
    {
        await SetAuthHeaderAsync();
        using var request = new HttpRequestMessage(method, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { Name = name, Type = PropertyTypeNames.Canonical(type), Value = value }),
                Encoding.UTF8,
                "application/json")
        };
        var response = await _httpClient.SendAsync(request);
        await EnsureSuccessCarryingTheReasonAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<bool> DeletePropertyAsync(Guid thingId, string propertyName)
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.DeleteAsync($"{_myceliumUrl}/api/things/{thingId}/properties/{Uri.EscapeDataString(propertyName)}");
        return response.IsSuccessStatusCode;
    }

    public virtual async Task<JsonElement> GetAllRelationshipsAsync()
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync($"{_myceliumUrl}/api/relationships");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement?> GetRelationshipAsync(Guid id)
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync($"{_myceliumUrl}/api/relationships/{id}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement> GetRelationshipRangesAsync(Guid id)
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync($"{_myceliumUrl}/api/relationships/{id}/ranges");
        await EnsureSuccessCarryingTheReasonAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement> GetRelationshipStatesAsync(Guid id)
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync($"{_myceliumUrl}/api/relationships/{id}/states");
        await EnsureSuccessCarryingTheReasonAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement> CreateRelationshipAsync(Guid subjectId, Guid predicateId, Guid targetId)
    {
        await SetAuthHeaderAsync();
        var content = new StringContent(
            JsonSerializer.Serialize(new { SubjectId = subjectId, PredicateId = predicateId, TargetId = targetId }),
            Encoding.UTF8,
            "application/json");
        var response = await _httpClient.PostAsync($"{_myceliumUrl}/api/relationships", content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<bool> DeleteRelationshipAsync(Guid id)
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.DeleteAsync($"{_myceliumUrl}/api/relationships/{id}");
        return response.IsSuccessStatusCode;
    }

    // A relationship's property write adds the property when the relationship does not hold it yet.
    public virtual Task<JsonElement> SetRelationshipPropertyAsync(Guid relId, string name, string type, object? value)
        => WritePropertyAsync(HttpMethod.Put, $"{_myceliumUrl}/api/relationships/{relId}/properties", name, type, value);

    public virtual async Task<bool> DeleteRelationshipPropertyAsync(Guid relId, string propertyName)
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.DeleteAsync($"{_myceliumUrl}/api/relationships/{relId}/properties/{Uri.EscapeDataString(propertyName)}");
        return response.IsSuccessStatusCode;
    }

    public virtual async Task<JsonElement> GetAllServicesAsync()
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync($"{_myceliumUrl}/api/mycelium/services");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // Every connection the model declares, each with what it resolves to. The platform resolves these —
    // a value may be held on the Thing, inherited through its `is` chain, or reached by an edge — so
    // reading them here is what keeps this client out of the business of resolving anything itself.
    public virtual async Task<JsonElement> GetAllConnectionsAsync()
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync($"{_myceliumUrl}/api/mycelium/connections");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<bool> ShutdownMyceliumAsync()
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.PostAsync($"{_myceliumUrl}/api/mycelium/shutdown", null);
        return response.IsSuccessStatusCode;
    }

    public virtual async Task<bool> StopServiceAsync(Guid handlerId)
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.PostAsync($"{_myceliumUrl}/api/mycelium/services/{handlerId}/stop", null);
        return response.IsSuccessStatusCode;
    }

    public virtual async Task<bool> StartServiceAsync(Guid handlerId)
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.PostAsync($"{_myceliumUrl}/api/mycelium/services/{handlerId}/start", null);
        return response.IsSuccessStatusCode;
    }

    public virtual async Task<string> GetModelJsonAsync()
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync($"{_myceliumUrl}/api/model");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public virtual async Task<string> SetModelAsync(string modelJson)
    {
        await SetAuthHeaderAsync();
        var content = new StringContent(modelJson, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_myceliumUrl}/api/model", content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public virtual async Task ClearModelAsync()
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.DeleteAsync($"{_myceliumUrl}/api/model");
        response.EnsureSuccessStatusCode();
    }

    // Upsert a fragment (a partial-model {Name, Things, Relationships} batch) into the live model.
    // Idempotent: re-applying the same fragment neither duplicates nor errors; the server resolves
    // lazy inheritance. Contrast SetModelAsync, which replaces the whole model.
    public virtual async Task<JsonElement> ApplyFragmentAsync(string fragmentJson)
    {
        await SetAuthHeaderAsync();
        var content = new StringContent(fragmentJson, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_myceliumUrl}/api/model/fragment", content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // Carry a group out of this model into a project model built for it from a template. The token names
    // the SOURCE, the opposite way round from a graft: the receiving model does not exist when the call
    // begins. Promoting the same group twice produces one project, because the server derives both the
    // project model's identifier and the identifiers inside it rather than generating them.
    public virtual async Task<JsonElement> PromoteAsync(
        Guid rootThingId, IReadOnlyList<string> followedPredicateNames, string template, string projectName)
    {
        await SetAuthHeaderAsync();
        var body = JsonSerializer.Serialize(new
        {
            RootThingId = rootThingId,
            FollowedPredicateNames = followedPredicateNames,
            Template = template,
            ProjectName = projectName,
        });
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_myceliumUrl}/api/model/promote", content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // Take a Thing and everything beneath it out of this model — the promotion's reach in reverse, so the
    // group a promotion carries out is the group a rejection clears. Members are retracted rather than
    // erased: the live model stops answering with them and a restart replays them back retracted.
    public virtual async Task<JsonElement> PruneAsync(
        Guid rootThingId, IReadOnlyList<string> followedPredicateNames)
    {
        await SetAuthHeaderAsync();
        var body = JsonSerializer.Serialize(new
        {
            RootThingId = rootThingId,
            FollowedPredicateNames = followedPredicateNames,
        });
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_myceliumUrl}/api/model/prune", content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // Upload an IFC file to the Xylem ingestion service, which parses it and applies the graph to the
    // model (mode: "merge" | "new-model"). Authenticated with a Mycelium token; returns the service's
    // result JSON ({ success, thingsCreated, thingsUpdated, relationshipsCreated, error }) for both
    // success and validation-failure responses so the caller can report either.
    public virtual async Task<JsonElement> IngestIfcAsync(string ingestUrl, string filePath, string modelName, string mode)
    {
        var token = await GetTokenAsync();
        using var form = new MultipartFormDataContent();
        await using var file = File.OpenRead(filePath);
        var fileContent = new StreamContent(file);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "file", Path.GetFileName(filePath));
        form.Add(new StringContent(modelName), "name");
        form.Add(new StringContent(mode), "mode");

        var request = new HttpRequestMessage(HttpMethod.Post, $"{ingestUrl.TrimEnd('/')}/ingest") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _httpClient.SendAsync(request);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement> GetEngineMetricsAsync()
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync($"{_myceliumUrl}/api/engines/metrics");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement> GetEngineReactorsAsync()
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync($"{_myceliumUrl}/api/engines/metrics/reactors");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement> GetSnapshotResolutionMetricsAsync()
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync($"{_myceliumUrl}/api/snapshots/resolution/metrics");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement> GetSeedStatusAsync()
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync($"{_myceliumUrl}/api/mycelium/startup-status");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement> ListLibrarySeedsAsync()
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync($"{_myceliumUrl}/api/mycelium/library-seeds");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement> LoadLibrarySeedAsync(string name)
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.PostAsync($"{_myceliumUrl}/api/mycelium/library-seeds/{Uri.EscapeDataString(name)}/load", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement> SaveLibrarySeedAsync(string name)
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.PutAsync($"{_myceliumUrl}/api/mycelium/library-seeds/{Uri.EscapeDataString(name)}", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement> ReloadSeedsAsync()
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.PostAsync($"{_myceliumUrl}/api/mycelium/seeds/reload", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement> GetEndpointsAsync()
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync($"{_myceliumUrl}/api/endpoints");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement> ListModelsAsync()
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync($"{_myceliumUrl}/api/models");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement> SwitchModelAsync(Guid modelId)
    {
        await SetAuthHeaderAsync();
        var content = new StringContent(
            JsonSerializer.Serialize(new { ModelId = modelId }),
            Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_myceliumUrl}/api/auth/switch-model", content);
        response.EnsureSuccessStatusCode();
        _cachedToken = null;
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (result.TryGetProperty("token", out var tokenElem))
        {
            _cachedToken = tokenElem.GetString();
            _tokenExpiry = _clock().AddMinutes(4);
        }
        return result;
    }

    public virtual async Task<JsonElement> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword)
    {
        await SetAuthHeaderAsync();
        var content = new StringContent(
            JsonSerializer.Serialize(new { CurrentPassword = currentPassword, NewPassword = newPassword }),
            Encoding.UTF8, "application/json");
        var response = await _httpClient.PutAsync($"{_myceliumUrl}/api/auth/users/{userId}/password", content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>One act of administering accounts, posted as the platform's Accounts page posts it: the
    /// act under <c>view</c>, the account under <c>record</c>, the values it asks for beside them. A
    /// refusal carries the route's own words, which is what an operator is told.</summary>
    public virtual async Task<JsonElement> AdministerAccountsAsync(object act)
    {
        await SetAuthHeaderAsync();
        var content = new StringContent(JsonSerializer.Serialize(act), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_myceliumUrl}/api/auth/administration", content);
        await EnsureSuccessCarryingTheReasonAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual Task<string> PostToEndpointAsync(string subdomain, string bodyJson)
        => PostBodyAsync($"{_myceliumUrl}/api/endpoints/{Uri.EscapeDataString(subdomain)}", bodyJson);

    public virtual Task<string> RequestServiceAsync(Guid handlerId, string bodyJson)
        => PostBodyAsync($"{_myceliumUrl}/api/mycelium/services/{handlerId}/request", bodyJson);

    private async Task<string> PostBodyAsync(string url, string bodyJson)
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.PostAsync(url, new StringContent(bodyJson, Encoding.UTF8, "application/json"));
        await EnsureSuccessCarryingTheReasonAsync(response);
        return await response.Content.ReadAsStringAsync();
    }

    public virtual async Task<JsonElement> GetDefaultPropertyModeAsync()
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync($"{_myceliumUrl}/api/config/property-mode");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement> SetDefaultPropertyModeAsync(string mode, int? ringBufferSize = null, int? sampleRate = null)
    {
        await SetAuthHeaderAsync();
        var payload = new Dictionary<string, object?> { ["Mode"] = mode };
        if (ringBufferSize.HasValue) payload["RingBufferSize"] = ringBufferSize.Value;
        if (sampleRate.HasValue) payload["SampleRate"] = sampleRate.Value;

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");
        var response = await _httpClient.PutAsync($"{_myceliumUrl}/api/config/property-mode", content);
        await EnsureSuccessCarryingTheReasonAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement> GetPropertyModeAsync(Guid thingId, string propertyName)
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync($"{_myceliumUrl}/api/things/{thingId}/properties/{Uri.EscapeDataString(propertyName)}/mode");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement> SetPropertyModeAsync(Guid thingId, string propertyName, string mode, int? ringBufferSize = null, int? sampleRate = null)
    {
        await SetAuthHeaderAsync();
        var payload = new Dictionary<string, object?> { ["Mode"] = mode };
        if (ringBufferSize.HasValue) payload["RingBufferSize"] = ringBufferSize.Value;
        if (sampleRate.HasValue) payload["SampleRate"] = sampleRate.Value;

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");
        var response = await _httpClient.PutAsync($"{_myceliumUrl}/api/things/{thingId}/properties/{Uri.EscapeDataString(propertyName)}/mode", content);
        await EnsureSuccessCarryingTheReasonAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // A refused mode is answered with the modes the platform accepts, and the default status-code
    // check throws that body away — leaving the operator "400 (Bad Request)" and nothing to act on.
    internal static async Task EnsureSuccessCarryingTheReasonAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;

        var body = (await response.Content.ReadAsStringAsync()).Trim();
        throw new HttpRequestException(body.Length == 0
            ? $"{(int)response.StatusCode} {response.ReasonPhrase}"
            : $"{(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }

    public virtual async Task<JsonElement> GetModelAtTimeAsync(DateTime? timestamp = null)
    {
        await SetAuthHeaderAsync();
        var url = $"{_myceliumUrl}/api/model";
        if (timestamp.HasValue)
        {
            url += $"?timestamp={timestamp.Value:O}";
        }
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement?> GetThingAtTimeAsync(Guid id, DateTime? timestamp = null)
    {
        await SetAuthHeaderAsync();
        var url = $"{_myceliumUrl}/api/things/{id}";
        if (timestamp.HasValue)
        {
            url += $"?timestamp={timestamp.Value:O}";
        }
        var response = await _httpClient.GetAsync(url);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement> GetPropertyVersionsAsync(
        Guid thingId,
        string propertyName,
        DateTime? startTime = null,
        DateTime? endTime = null)
    {
        await SetAuthHeaderAsync();
        var url = $"{_myceliumUrl}/api/things/{thingId}/properties/{Uri.EscapeDataString(propertyName)}/versions{BuildTimeRangeQuery(startTime, endTime)}";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement> GetThingMutationsAsync(
        Guid thingId,
        DateTime? startTime = null,
        DateTime? endTime = null)
    {
        await SetAuthHeaderAsync();
        var url = $"{_myceliumUrl}/api/things/{thingId}/mutations{BuildTimeRangeQuery(startTime, endTime)}";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement> GetModelMutationsAsync(
        DateTime? startTime = null,
        DateTime? endTime = null)
    {
        await SetAuthHeaderAsync();
        var url = $"{_myceliumUrl}/api/mutations{BuildTimeRangeQuery(startTime, endTime)}";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement> GetRelationshipMutationsAsync(
        Guid relationshipId,
        DateTime? startTime = null,
        DateTime? endTime = null)
    {
        await SetAuthHeaderAsync();
        var url = $"{_myceliumUrl}/api/relationships/{relationshipId}/mutations{BuildTimeRangeQuery(startTime, endTime)}";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement> CreateRangeAsync(
        Guid thingId,
        string name,
        string criteria,
        string? property = null,
        object? bounds = null)
    {
        await SetAuthHeaderAsync();
        var payload = new Dictionary<string, object?> { ["Name"] = name, ["Criteria"] = criteria };
        if (property != null) payload["Property"] = property;
        if (bounds != null) payload["Bounds"] = bounds;

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");
        var response = await _httpClient.PostAsync($"{_myceliumUrl}/api/things/{thingId}/ranges", content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement> GetRangesAsync(Guid thingId)
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync($"{_myceliumUrl}/api/things/{thingId}/ranges");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement?> GetRangeAsync(Guid thingId, string rangeName)
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync($"{_myceliumUrl}/api/things/{thingId}/ranges/{Uri.EscapeDataString(rangeName)}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<bool> DeleteRangeAsync(Guid thingId, string rangeName)
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.DeleteAsync($"{_myceliumUrl}/api/things/{thingId}/ranges/{Uri.EscapeDataString(rangeName)}");
        return response.IsSuccessStatusCode;
    }

    public virtual async Task<JsonElement> GetStatesAsync(Guid thingId)
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync($"{_myceliumUrl}/api/things/{thingId}/states");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement> GetStateTransitionsAsync(Guid thingId, DateTime? from, DateTime? to)
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync(
            $"{_myceliumUrl}/api/things/{thingId}/state-transitions{Query(("from", from?.ToString("O")), ("to", to?.ToString("O")))}");
        await EnsureSuccessCarryingTheReasonAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement> GetStateOccurrencesAsync(Guid thingId, string stateName, DateTime? from, DateTime? to)
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync(
            $"{_myceliumUrl}/api/things/{thingId}/states/{Uri.EscapeDataString(stateName)}/occurrences"
            + Query(("from", from?.ToString("O")), ("to", to?.ToString("O"))));
        await EnsureSuccessCarryingTheReasonAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<JsonElement> ValidateCriteriaAsync(string criteria)
    {
        await SetAuthHeaderAsync();
        var payload = new Dictionary<string, string> { ["Criteria"] = criteria };
        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");
        var response = await _httpClient.PostAsync($"{_myceliumUrl}/api/ranges/validate", content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // The narrowing is applied by the server, so the answer is the size of the caller's decision
    // rather than the size of the model. Kinds — the Things others `is` — are absent unless
    // includeArchetypes asks for them, which is the one thing that changes for a caller naming
    // nothing.
    public virtual async Task<JsonElement> GetThingsInStateAsync(string stateName, StateListNarrowing? narrowing = null)
    {
        await SetAuthHeaderAsync();
        var asked = narrowing ?? new StateListNarrowing();
        var response = await _httpClient.GetAsync(
            $"{_myceliumUrl}/api/states/{Uri.EscapeDataString(stateName)}/things" + Query(
                ("alsoIn", asked.AlsoIn),
                ("notIn", asked.NotIn),
                ("type", asked.Type),
                ("within", asked.Within?.ToString()),
                ("includeArchetypes", asked.IncludeArchetypes ? "true" : null),
                ("limit", asked.Limit?.ToString()),
                ("properties", asked.Properties),
                ("countOnly", asked.CountOnly ? "true" : null)));
        await EnsureSuccessCarryingTheReasonAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}

// What a list read may be narrowed by, in the platform's own words: the kind a Thing `is`, the container
// that reaches it, the most to answer, the properties to send, and the names to look up instead of listing.
public sealed record ThingListNarrowing(
    string? Type = null, Guid? Within = null, int? Limit = null, string? Properties = null, string? Names = null)
{
    public bool IsEmpty => Type is null && Within is null && Limit is null && Properties is null && Names is null;
}

public sealed record StateListNarrowing(
    string? AlsoIn = null,
    string? NotIn = null,
    string? Type = null,
    Guid? Within = null,
    bool IncludeArchetypes = false,
    int? Limit = null,
    string? Properties = null,
    bool CountOnly = false);
