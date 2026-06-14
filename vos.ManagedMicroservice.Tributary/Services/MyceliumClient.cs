using System.Net.Http.Json;
using System.Text.Json;
using vos.ManagedMicroservice.Shared;

namespace vos.ManagedMicroservice.Tributary.Services;

/// <summary>
/// HTTP client for communicating with the VOS Mycelium.
/// </summary>
public class MyceliumClient : MyceliumClientBase, IEndpointMyceliumClient
{
    public MyceliumClient(IHttpClientFactory httpClientFactory, ILogger<MyceliumClient> logger, string myceliumUrl, string? serviceToken = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken) { }

    public readonly record struct MyceliumThing(Guid Id, string Name);

    public readonly record struct EndpointConfig(string Url, string HttpMethod);

    public async Task<MyceliumThing?> FindThingByNameAsync(string name)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10));
            var encodedName = Uri.EscapeDataString(name);
            var response = await client.GetAsync($"{MyceliumUrl}/api/things?name={encodedName}");
            if (!response.IsSuccessStatusCode)
            {
                Logger.LogWarning("Failed to find thing by name {Name}. Status: {StatusCode}", name, response.StatusCode);
                return null;
            }

            var root = await response.Content.ReadFromJsonAsync<JsonElement>();
            return TryParseThing(root, out var thing) ? thing : null;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error finding thing by name {Name}", name);
            return null;
        }
    }

    public async Task<EndpointConfig?> GetEndpointConfigByNameAsync(string endpointName)
    {
        var thing = await FindThingByNameAsync(endpointName);
        if (thing == null)
            return null;

        var effective = await GetEffectivePropertiesAsync(thing.Value.Id);
        if (effective == null)
            return null;

        var url = TryGetString(effective, "url");
        var method = TryGetString(effective, "httpMethod");

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(method))
        {
            Logger.LogWarning("Thing {Name} missing required Endpoint properties (url/httpMethod)", endpointName);
            return null;
        }

        return new EndpointConfig(url!, method!);
    }

    public async Task<MyceliumThing?> CreateThingAsync(string name, Dictionary<string, object?>? properties = null)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10));
            var payload = new
            {
                Name = name,
                Properties = properties
            };

            var response = await client.PostAsJsonAsync($"{MyceliumUrl}/api/things", payload);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                Logger.LogWarning("Failed to create thing {ThingName}. Status: {StatusCode}. Error: {Error}", name, response.StatusCode, error);
                return null;
            }

            var root = await response.Content.ReadFromJsonAsync<JsonElement>();
            return TryParseThing(root, out var thing) ? thing : null;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error creating thing {ThingName}", name);
            return null;
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

    public async Task<Dictionary<string, JsonElement>?> GetEffectivePropertiesAsync(Guid thingId)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10));
            var response = await client.GetAsync($"{MyceliumUrl}/api/things/{thingId}/effective-properties");
            if (!response.IsSuccessStatusCode)
            {
                Logger.LogWarning("Failed to get effective properties for thing {ThingId}. Status: {StatusCode}",
                    thingId, response.StatusCode);
                return null;
            }

            var root = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in root.EnumerateObject())
            {
                if (TryGetPropertyCaseInsensitive(property.Value, "Value", out var valueElement))
                    result[property.Name] = valueElement;
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error getting effective properties for thing {ThingId}", thingId);
            return null;
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

        thing = new MyceliumThing(id, nameProp.GetString() ?? string.Empty);
        return true;
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.TryGetProperty(propertyName, out value))
            return true;

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? TryGetString(Dictionary<string, JsonElement> properties, string name)
    {
        if (!properties.TryGetValue(name, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => value.ToString()
        };
    }

    private static (object? Value, string Type) ResolveMyceliumValue(object? value)
    {
        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.String => (je.GetString(), "System.String"),
                JsonValueKind.Number when je.TryGetInt64(out var l) => (l, "System.Int64"),
                JsonValueKind.Number => (je.GetDouble(), "System.Double"),
                JsonValueKind.True => (true, "System.Boolean"),
                JsonValueKind.False => (false, "System.Boolean"),
                JsonValueKind.Null => (null, "System.String"),
                _ => (je.GetRawText(), "System.String")
            };
        }

        var type = value switch
        {
            string => "System.String",
            int => "System.Int32",
            long => "System.Int64",
            double => "System.Double",
            decimal => "System.Decimal",
            bool => "System.Boolean",
            _ => "System.String"
        };

        return (value, type);
    }
}
