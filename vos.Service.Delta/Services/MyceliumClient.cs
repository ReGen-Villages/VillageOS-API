using System.Net.Http.Json;
using System.Text.Json;
using vos.Service.Shared;
using vos.Service.Delta.Models;

namespace vos.Service.Delta.Services;

public class MyceliumClient : MyceliumClientBase
{
    public MyceliumClient(IHttpClientFactory httpClientFactory, ILogger<MyceliumClient> logger, string myceliumUrl, string? serviceToken = null, string? apiKey = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken, apiKey: apiKey) { }

    public readonly record struct MyceliumThing(Guid Id, string Name, Dictionary<string, object?> Properties);

    public async Task<IReadOnlyDictionary<string, JsonElement>?> GetResolvedPropertiesAsync(Guid thingId)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10));
            var response = await client.GetAsync($"{MyceliumUrl}{MyceliumRoutes.ThingProperties(thingId)}");
            if (!response.IsSuccessStatusCode)
            {
                Logger.LogWarning("Failed to read the properties of thing {ThingId}. Status: {StatusCode}",
                    thingId, response.StatusCode);
                return null;
            }

            var root = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var resolved = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in root.EnumerateObject())
                if (TryGetPropertyCaseInsensitive(property.Value, "Value", out var value))
                    resolved[property.Name] = value;
            return resolved;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error reading the properties of thing {ThingId}", thingId);
            return null;
        }
    }

    public async Task<MyceliumThing?> CreateThingAsync(RegisterEndpointRequest dto)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10));
            // Not the request as it arrived: a caller states a template's keys as bare values, and the
            // broker takes each one with its type on it. The rest of the request is what the create
            // route reads — it works relationships out from the relationships Delta writes afterwards.
            var response = await client.PostAsJsonAsync($"{MyceliumUrl}/api/things",
                new { dto.Name, Properties = TypedProperties.Typed(dto.Properties) });
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                Logger.LogWarning("Failed to create thing {ThingName}. Status: {StatusCode}. Error: {Error}", dto.Name, response.StatusCode, error);
                return null;
            }

            var root = await response.Content.ReadFromJsonAsync<JsonElement>();
            return TryParseThing(root, out var thing) ? thing : null;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error creating thing {ThingName}", dto.Name);
            return null;
        }
    }

    public async Task<bool> DeleteThingAsync(Guid thingId)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10));
            var response = await client.DeleteAsync($"{MyceliumUrl}/api/things/{thingId}");
            if (response.IsSuccessStatusCode)
                return true;

            var error = await response.Content.ReadAsStringAsync();
            Logger.LogWarning("Failed to delete thing {ThingId}. Status: {StatusCode}. Error: {Error}",
                thingId, response.StatusCode, error);
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting thing {ThingId}", thingId);
            return false;
        }
    }

    public async Task<bool> SetThingPropertyAsync(Guid thingId, string name, object? value)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10));
            var (resolved, type) = ResolveMyceliumValue(value);
            var payload = new
            {
                Name = name,
                Type = type,
                Value = resolved
            };

            var response = await client.PutAsJsonAsync($"{MyceliumUrl}/api/things/{thingId}/properties", payload);
            if (response.IsSuccessStatusCode)
                return true;

            var error = await response.Content.ReadAsStringAsync();
            Logger.LogWarning(
                "Failed to set property {PropName} on thing {ThingId}. Status: {StatusCode}. Error: {Error}",
                name, thingId, response.StatusCode, error);
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error setting property {PropName} on thing {ThingId}", name, thingId);
            return false;
        }
    }

    public async Task<bool> CreateRelationshipAsync(Guid subjectId, Guid predicateId, Guid targetId)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10));
            var payload = new { subjectId, predicateId, targetId };

            var response = await client.PostAsJsonAsync($"{MyceliumUrl}/api/relationships", payload);
            if (response.IsSuccessStatusCode)
                return true;

            var error = await response.Content.ReadAsStringAsync();
            Logger.LogWarning(
                "Failed to create relationship {Subject} -[{Predicate}]-> {Target}. Status: {Status}. Error: {Error}",
                subjectId, predicateId, targetId, response.StatusCode, error);
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "Error creating relationship {Subject} -[{Predicate}]-> {Target}",
                subjectId, predicateId, targetId);
            return false;
        }
    }

    private static bool TryParseThing(JsonElement element, out MyceliumThing? thing)
    {
        thing = null;

        if (element.ValueKind != JsonValueKind.Object)
            return false;

        if (!TryGetPropertyCaseInsensitive(element, "Id", out var idProp) || idProp.ValueKind != JsonValueKind.String)
            return false;

        if (!Guid.TryParse(idProp.GetString(), out var id))
            return false;

        if (!TryGetPropertyCaseInsensitive(element, "Name", out var nameProp) || nameProp.ValueKind != JsonValueKind.String)
            return false;

        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (TryGetPropertyCaseInsensitive(element, "Properties", out var propertiesProp) && propertiesProp.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in propertiesProp.EnumerateObject())
            {
                properties[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.TryGetInt64(out var l) ? l : property.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => property.Value.GetRawText()
                };
            }
        }

        thing = new MyceliumThing(id, nameProp.GetString() ?? string.Empty, properties);
        return true;
    }

    // A request's values arrive as JsonElement; one with structure is written as its JSON text.
    private static (object? Value, string Type) ResolveMyceliumValue(object? value)
    {
        var unwrapped = value is JsonElement element
            ? element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.GetRawText(),
            }
            : value;

        return (unwrapped, TypedProperties.TypeNameFor(unwrapped));
    }
}
