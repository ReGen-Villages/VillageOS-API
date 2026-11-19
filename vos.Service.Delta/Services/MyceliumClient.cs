using System.Text.Json;
using vos.Service.Shared;
using vos.Service.Delta.Models;

namespace vos.Service.Delta.Services;

public class MyceliumClient : MyceliumClientBase
{
    public MyceliumClient(IHttpClientFactory httpClientFactory, ILogger<MyceliumClient> logger, string myceliumUrl, LaunchToken? launchToken = null, string? apiKey = null)
        : base(httpClientFactory, logger, myceliumUrl, launchToken, apiKey: apiKey) { }

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
            // Not the request as it arrived: a caller states a template's keys as bare values, and the
            // broker takes each one with its type on it. The rest of the request is what the create
            // route reads — it works relationships out from the relationships Delta writes afterwards.
            var answer = await AskToCreateThingAsync(
                JsonContent.Create(new { dto.Name, Properties = TypedProperties.Typed(dto.Properties) }),
                TimeSpan.FromSeconds(10), CancellationToken.None);
            if (!answer.IsDone)
            {
                Logger.LogWarning("Failed to create thing {ThingName}. Status: {StatusCode}. Error: {Error}", dto.Name, answer.Status, answer.Body);
                return null;
            }

            return TryParseThing(answer.Json(), out var thing) ? thing : null;
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
            var answer = await AskToDeleteThingAsync(thingId, TimeSpan.FromSeconds(10), CancellationToken.None);
            if (answer.IsDone)
                return true;

            Logger.LogWarning("Failed to delete thing {ThingId}. Status: {StatusCode}. Error: {Error}",
                thingId, answer.Status, answer.Body);
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

    public Task<bool> CreateRelationshipAsync(
        Guid subjectId, Guid predicateId, Guid targetId, CancellationToken cancellationToken = default) =>
        PostRelationshipAsync(subjectId, predicateId, targetId, TimeSpan.FromSeconds(10), cancellationToken);

    private static bool TryParseThing(JsonElement element, out MyceliumThing? thing)
    {
        thing = null;
        if (!TryReadThing(element, out var id, out var name))
            return false;

        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (TryGetPropertyCaseInsensitive(element, "Properties", out var propertiesProp) && propertiesProp.ValueKind == JsonValueKind.Object)
            foreach (var property in propertiesProp.EnumerateObject())
                properties[property.Name] = PlainValueOf(property.Value);

        thing = new MyceliumThing(id, name, properties);
        return true;
    }

    // A request's values arrive as JsonElement; one with structure is written as its JSON text.
    private static (object? Value, string Type) ResolveMyceliumValue(object? value)
    {
        var unwrapped = value is JsonElement element ? PlainValueOf(element) : value;
        return (unwrapped, TypedProperties.TypeNameFor(unwrapped));
    }

    private static object? PlainValueOf(JsonElement element) =>
        JsonScalar.TryRead(element, out var scalar) ? scalar : element.GetRawText();
}
