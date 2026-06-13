using System.Text.Json;

namespace vos.Taproot;

/// <summary>
/// Resolves thing names or IDs to GUIDs and vice versa.
/// Supports both explicit GUIDs and name-based lookups.
/// Names must be unambiguous (unique) for resolution to succeed.
/// </summary>
public class NameResolver
{
    private readonly BrokerClient _broker;
    private JsonElement? _cachedThings;
    private Dictionary<string, string>? _guidToNameMap;

    public NameResolver(BrokerClient broker)
    {
        _broker = broker;
    }

    /// <summary>
    /// Clears the cached things, forcing a fresh fetch on next resolution.
    /// </summary>
    public void ClearCache()
    {
        _cachedThings = null;
        _guidToNameMap = null;
    }

    /// <summary>
    /// Resolves a name or ID string to a GUID.
    /// </summary>
    /// <param name="nameOrId">A GUID string or thing name</param>
    /// <returns>A result containing the resolved GUID or an error message</returns>
    public async Task<ResolveResult> ResolveThingAsync(string nameOrId)
    {
        // First, try to parse as GUID (maintains backwards compatibility)
        if (Guid.TryParse(nameOrId, out var id))
        {
            return ResolveResult.Success(id);
        }

        // Not a GUID, so try to look up by name
        return await ResolveByNameAsync(nameOrId);
    }

    /// <summary>
    /// Resolves multiple names or IDs in a single call, using a single API fetch.
    /// </summary>
    public async Task<ResolveResult[]> ResolveThingsAsync(params string[] namesOrIds)
    {
        // Pre-fetch things once for all resolutions
        await EnsureThingsCachedAsync();

        var results = new ResolveResult[namesOrIds.Length];
        for (int i = 0; i < namesOrIds.Length; i++)
        {
            results[i] = await ResolveThingAsync(namesOrIds[i]);
        }
        return results;
    }

    /// <summary>
    /// Resolves a GUID to a thing name. Returns the GUID string if name not found.
    /// </summary>
    public async Task<string> ResolveNameAsync(string guidString)
    {
        await EnsureGuidToNameMapAsync();

        if (_guidToNameMap != null &&
            _guidToNameMap.TryGetValue(guidString.ToLowerInvariant(), out var name))
        {
            return name;
        }

        return guidString;
    }

    /// <summary>
    /// Resolves a GUID to a thing name. Returns the GUID string if name not found.
    /// </summary>
    public async Task<string> ResolveNameAsync(Guid guid)
    {
        return await ResolveNameAsync(guid.ToString());
    }

    /// <summary>
    /// Builds a dictionary mapping GUIDs to names for quick lookup.
    /// </summary>
    public async Task<Dictionary<string, string>> GetGuidToNameMapAsync()
    {
        await EnsureGuidToNameMapAsync();
        return _guidToNameMap ?? new Dictionary<string, string>();
    }

    private async Task EnsureThingsCachedAsync()
    {
        if (_cachedThings == null)
        {
            _cachedThings = await _broker.GetAllThingsAsync();
        }
    }

    private async Task EnsureGuidToNameMapAsync()
    {
        if (_guidToNameMap != null)
            return;

        await EnsureThingsCachedAsync();

        _guidToNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (_cachedThings?.ValueKind == JsonValueKind.Array)
        {
            foreach (var thing in _cachedThings.Value.EnumerateArray())
            {
                var id = GetProperty(thing, "Id") ?? GetProperty(thing, "id");
                var name = GetProperty(thing, "Name") ?? GetProperty(thing, "name");

                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                {
                    _guidToNameMap[id.ToLowerInvariant()] = name;
                }
            }
        }
    }

    private async Task<ResolveResult> ResolveByNameAsync(string name)
    {
        await EnsureThingsCachedAsync();

        if (_cachedThings?.ValueKind != JsonValueKind.Array)
        {
            return ResolveResult.Error($"No things found. Cannot resolve name '{name}'.");
        }

        var matches = new List<(Guid Id, string Name)>();

        foreach (var thing in _cachedThings.Value.EnumerateArray())
        {
            var thingName = GetProperty(thing, "Name") ?? GetProperty(thing, "name") ?? "";

            if (thingName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                var idStr = GetProperty(thing, "Id") ?? GetProperty(thing, "id");
                if (idStr != null && Guid.TryParse(idStr, out var thingId))
                {
                    matches.Add((thingId, thingName));
                }
            }
        }

        return matches.Count switch
        {
            0 => ResolveResult.Error($"No thing found with name '{name}'."),
            1 => ResolveResult.Success(matches[0].Id),
            _ => ResolveResult.Error($"Ambiguous name '{name}': found {matches.Count} things with this name. Use the ID instead:\n" +
                                     string.Join("\n", matches.Select(m => $"  {m.Id} - {m.Name}")))
        };
    }

    private static string? GetProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }
        return null;
    }
}

/// <summary>
/// Result of a name resolution attempt.
/// </summary>
public readonly struct ResolveResult
{
    public bool IsSuccess { get; }
    public Guid Id { get; }
    public string? ErrorMessage { get; }

    private ResolveResult(bool success, Guid id, string? errorMessage)
    {
        IsSuccess = success;
        Id = id;
        ErrorMessage = errorMessage;
    }

    public static ResolveResult Success(Guid id) => new(true, id, null);
    public static ResolveResult Error(string message) => new(false, Guid.Empty, message);
}
