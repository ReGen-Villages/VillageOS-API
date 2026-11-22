using System.Text.Json;

namespace vos.PlatformClient;

public class NameResolver
{
    private readonly MyceliumClient _mycelium;
    private JsonElement? _cachedThings;
    private Dictionary<string, string>? _guidToNameMap;

    public NameResolver(MyceliumClient mycelium)
    {
        _mycelium = mycelium;
    }

    public async Task<ResolveResult> ResolveThingAsync(string nameOrId)
    {
        if (Guid.TryParse(nameOrId, out var id))
        {
            return ResolveResult.Success(id);
        }

        return await ResolveByNameAsync(nameOrId);
    }

    // Asks the platform for the one name, where ResolveThingAsync reads every Thing to find it.
    public async Task<ResolveResult> FindThingAsync(string nameOrId)
    {
        if (Guid.TryParse(nameOrId, out var id))
            return ResolveResult.Success(id);

        if (nameOrId.Contains(','))
            return ResolveResult.Error(
                $"'{nameOrId}' holds a comma, and the platform reads a comma as separating names. Give the Thing's identifier.");

        var found = await _mycelium.GetThingByNameAsync(nameOrId);
        if (found.Thing is { } thing)
            return ResolveResult.Success(thing.GetProperty("Id").GetGuid());

        return found.Matching.Count == 0
            ? ResolveResult.Error($"No thing found with name '{nameOrId}'.")
            : ResolveResult.Error(
                $"Ambiguous name '{nameOrId}': found {found.Matching.Count} things with this name. Use the ID instead:\n"
                + string.Join("\n", found.Matching.Select(matching => $"  {matching}")));
    }

    public async Task<ResolveResult[]> ResolveThingsAsync(params string[] namesOrIds)
    {
        await EnsureThingsCachedAsync();

        var results = new ResolveResult[namesOrIds.Length];
        for (int i = 0; i < namesOrIds.Length; i++)
        {
            results[i] = await ResolveThingAsync(namesOrIds[i]);
        }
        return results;
    }

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

    public async Task<string> ResolveNameAsync(Guid guid)
    {
        return await ResolveNameAsync(guid.ToString());
    }

    public async Task<Dictionary<string, string>> GetGuidToNameMapAsync()
    {
        await EnsureGuidToNameMapAsync();
        return _guidToNameMap ?? new Dictionary<string, string>();
    }

    private async Task EnsureThingsCachedAsync()
    {
        if (_cachedThings == null)
        {
            _cachedThings = await _mycelium.GetAllThingsAsync();
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

            if (thingName.Equals(name, StringComparison.Ordinal))
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
