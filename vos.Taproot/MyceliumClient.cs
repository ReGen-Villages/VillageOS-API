using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

[assembly: InternalsVisibleTo("vos.Taproot.Tests")]

namespace vos.Taproot;

public class MyceliumClient
{
    private readonly HttpClient _httpClient;
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
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    public virtual async Task<string> GetTokenAsync()
    {
        if (_cachedToken != null && _clock() < _tokenExpiry)
        {
            return _cachedToken;
        }

        if (string.IsNullOrEmpty(_apiKey))
        {
            throw new InvalidOperationException(
                "No API key configured. Set VOS_API_KEY environment variable or pass --api-key flag.");
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

    private static string BuildTimeRangeQuery(DateTime? startTime, DateTime? endTime)
    {
        var queryParams = new List<string>();
        if (startTime.HasValue)
            queryParams.Add($"startTime={startTime.Value:O}");
        if (endTime.HasValue)
            queryParams.Add($"endTime={endTime.Value:O}");

        return queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "";
    }

    public virtual async Task<JsonElement> GetAllThingsAsync()
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync($"{_myceliumUrl}/api/things");
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

    public virtual async Task<JsonElement> SetPropertyAsync(Guid thingId, string name, string type, object? value)
    {
        await SetAuthHeaderAsync();
        var content = new StringContent(
            JsonSerializer.Serialize(new { Name = name, Type = type, Value = value }),
            Encoding.UTF8,
            "application/json");
        var response = await _httpClient.PutAsync($"{_myceliumUrl}/api/things/{thingId}/properties", content);
        response.EnsureSuccessStatusCode();
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

    public virtual async Task<JsonElement> SetRelationshipPropertyAsync(Guid relId, string name, string type, object? value)
    {
        await SetAuthHeaderAsync();
        var content = new StringContent(
            JsonSerializer.Serialize(new { Name = name, Type = type, Value = value }),
            Encoding.UTF8, "application/json");
        var response = await _httpClient.PutAsync($"{_myceliumUrl}/api/relationships/{relId}/properties", content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

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

    public virtual async Task<JsonElement> GetAllDaemonsAsync()
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync($"{_myceliumUrl}/api/mycelium/daemons");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public virtual async Task<bool> StopDaemonAsync(string daemonKey)
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.PostAsync($"{_myceliumUrl}/api/mycelium/daemons/{Uri.EscapeDataString(daemonKey)}/stop", null);
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

    public virtual async Task<JsonElement> GetSeedStatusAsync()
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync($"{_myceliumUrl}/api/mycelium/seed-status");
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
        response.EnsureSuccessStatusCode();
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
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
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

    public virtual async Task<JsonElement> GetThingsInStateAsync(string stateName)
    {
        await SetAuthHeaderAsync();
        var response = await _httpClient.GetAsync($"{_myceliumUrl}/api/states/{Uri.EscapeDataString(stateName)}/things");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
