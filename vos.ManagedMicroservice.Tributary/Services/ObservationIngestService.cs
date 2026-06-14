using System.Text.Json;
using Jsonata.Net.Native;

namespace vos.ManagedMicroservice.Tributary.Services;

public record ObservationIngestResult(
    bool Success,
    int ObservedCount,
    string? Error,
    string? Detail,
    int? Index = null,
    Guid? ObservationThingId = null);

public class ObservationIngestService
{
    private readonly IEndpointMyceliumClient _myceliumClient;
    private readonly ILogger<ObservationIngestService> _logger;

    public ObservationIngestService(IEndpointMyceliumClient myceliumClient, ILogger<ObservationIngestService> logger)
    {
        _myceliumClient = myceliumClient;
        _logger = logger;
    }

    public bool TryTransform(string body, JsonataQuery query, out string transformed, out string error)
    {
        transformed = string.Empty;
        error = string.Empty;

        try
        {
            try
            {
                using var _ = JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                error = "Endpoint response is not valid JSON.";
                return false;
            }

            var rawResult = query.Eval(body);
            if (string.IsNullOrWhiteSpace(rawResult))
            {
                transformed = "null";
                return true;
            }

            if (TryNormalizeJson(rawResult, out var normalized))
            {
                transformed = normalized;
                return true;
            }

            transformed = JsonSerializer.Serialize(rawResult);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public async Task<ObservationIngestResult> CreateObservationsAsync(Guid endpointThingId, JsonataQuery query, string body)
    {
        if (!TryTransform(body, query, out var transformed, out var transformError))
        {
            return new ObservationIngestResult(false, 0, "Endpoint response transform failed", transformError);
        }

        if (!TryParseObservationPayloads(transformed, out var observations, out var parseError))
        {
            return new ObservationIngestResult(false, 0, "Transformed output is not a valid observation thing array.", parseError);
        }

        var observedPredicate = await _myceliumClient.FindThingByNameAsync("observed");
        if (observedPredicate == null)
            observedPredicate = await _myceliumClient.CreateThingAsync("observed");

        if (observedPredicate == null)
            return new ObservationIngestResult(false, 0, "Failed to resolve or create 'observed' predicate.", null);

        var createdCount = 0;
        for (var i = 0; i < observations.Count; i++)
        {
            var obs = observations[i];
            var created = await _myceliumClient.CreateThingAsync(obs.Name, obs.Properties);
            if (created == null)
            {
                return new ObservationIngestResult(false, createdCount, "Failed to create observation thing.", null, i);
            }

            var related = await _myceliumClient.CreateRelationshipAsync(
                endpointThingId,
                observedPredicate.Value.Id,
                created.Value.Id);

            if (!related)
            {
                return new ObservationIngestResult(false, createdCount, "Failed to relate observation thing to endpoint.", null, i, created.Value.Id);
            }

            createdCount++;
        }

        return new ObservationIngestResult(true, createdCount, null, null);
    }

    private static bool TryNormalizeJson(string input, out string normalized)
    {
        try
        {
            using var doc = JsonDocument.Parse(input);
            normalized = JsonSerializer.Serialize(doc.RootElement);
            return true;
        }
        catch (JsonException)
        {
            normalized = string.Empty;
            return false;
        }
    }

    private static bool TryParseObservationPayloads(string json, out List<ObservationPayload> observations, out string error)
    {
        observations = new List<ObservationPayload>();
        error = string.Empty;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                return TryParseObservation(doc.RootElement, observations, out error);
            }

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                error = "Transformed output must be a JSON object or array.";
                return false;
            }

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (!TryParseObservation(element, observations, out error))
                    return false;
            }
        }

        return true;
    }

    private static bool TryParseObservation(JsonElement element, List<ObservationPayload> observations, out string error)
    {
        error = string.Empty;

        if (element.ValueKind != JsonValueKind.Object)
        {
            error = "Each observation must be a JSON object.";
            return false;
        }

        if (!element.TryGetProperty("name", out var nameProp) || nameProp.ValueKind != JsonValueKind.String)
        {
            error = "Observation is missing a string 'name' property.";
            return false;
        }

        if (!element.TryGetProperty("properties", out var propsProp) || propsProp.ValueKind != JsonValueKind.Object)
        {
            error = "Observation is missing an object 'properties' property.";
            return false;
        }

        var props = JsonSerializer.Deserialize<Dictionary<string, object?>>(propsProp.GetRawText())
                    ?? new Dictionary<string, object?>();

        observations.Add(new ObservationPayload(nameProp.GetString() ?? string.Empty, props));
        return true;
    }

    private record ObservationPayload(string Name, Dictionary<string, object?> Properties);
}
