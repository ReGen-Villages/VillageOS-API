using System.Net.Http.Json;
using System.Text.Json;
using vos.Microservice.Shared;
using vos.ManagedMicroservice.IntegrationRegistry.Models;

namespace vos.ManagedMicroservice.IntegrationRegistry.Services;

/// <summary>
/// HTTP client for communicating with the VOS Broker.
/// Extends BrokerClientBase for shared token management.
/// Adds IntegrationRegistry-specific operations (thing/relationship CRUD).
/// </summary>
public class BrokerClient : BrokerClientBase
{
    public BrokerClient(IHttpClientFactory httpClientFactory, ILogger<BrokerClient> logger, string brokerUrl, string? serviceToken = null)
        : base(httpClientFactory, logger, brokerUrl, serviceToken) { }

    public readonly record struct BrokerThing(Guid Id, string Name, Dictionary<string, object?> Properties);

    public async Task<BrokerThing?> FindThingByNameAsync(string name)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10));
            var encodedName = Uri.EscapeDataString(name);
            var response = await client.GetAsync($"{BrokerUrl}/api/things?name={encodedName}");
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

    public async Task<BrokerThing?> CreateThingAsync(RegisterEndpointRequest dto)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10));
            var response = await client.PostAsJsonAsync($"{BrokerUrl}/api/things", dto);
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
            var response = await client.DeleteAsync($"{BrokerUrl}/api/things/{thingId}");
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
            var (resolved, type) = ResolveBrokerValue(value);
            var payload = new
            {
                Name = name,
                Type = type,
                Value = resolved
            };

            var response = await client.PutAsJsonAsync($"{BrokerUrl}/api/things/{thingId}/properties", payload);
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

            var response = await client.PostAsJsonAsync($"{BrokerUrl}/api/relationships", payload);
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

    private static bool TryParseThing(JsonElement element, out BrokerThing? thing)
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

        thing = new BrokerThing(id, nameProp.GetString() ?? string.Empty, properties);
        return true;
    }

    /// <summary>
    /// Unwraps a potential JsonElement (produced by System.Text.Json when deserializing
    /// Dictionary&lt;string, object&gt;) into a CLR value and infers the broker type string.
    /// </summary>
    private static (object? Value, string Type) ResolveBrokerValue(object? value)
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
}
