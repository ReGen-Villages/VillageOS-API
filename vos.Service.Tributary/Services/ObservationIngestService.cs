using System.Globalization;
using System.Text.Json;
using vos.Service.Shared;

namespace vos.Service.Tributary.Services;

public record ObservationIngestResult(
    bool Success,
    int EntitiesTouched,
    int ObservationsSubmitted,
    string? Error,
    string? Detail);

// Ingests fetched-and-reshaped readings as time-series observations (Phase 5b hybrid, #5587).
// Each reading names an entity and carries a bag of property values at an observed time. Entities
// become structural Things — created once, related to the source endpoint once — so the graph
// scales with the number of entities, not readings; the readings themselves are written as
// observations on each entity's property series (Canopy → Sapwood), bounded by PropertyMode.
public class ObservationIngestService
{
    // Retention applied to newly-declared observed properties. Sampled bounds storage
    // growth at the source — the right default for high-volume sediment series.
    public const string DefaultObservationMode = "Sampled";

    private readonly IEndpointMyceliumClient _myceliumClient;
    private readonly ILogger<ObservationIngestService> _logger;

    public ObservationIngestService(IEndpointMyceliumClient myceliumClient, ILogger<ObservationIngestService> logger)
    {
        _myceliumClient = myceliumClient;
        _logger = logger;
    }

    public bool TryTransform(string body, JsonataTransform query, out string transformed, out string error)
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

    // A supplied subjectId takes the reading's own name out of play entirely: nothing is resolved
    // or created from it, whether or not the expression produced one.
    public async Task<ObservationIngestResult> CreateObservationsAsync(
        Guid endpointThingId, JsonataTransform query, string body, Guid? subjectId = null)
    {
        if (!TryTransform(body, query, out var transformed, out var transformError))
            return new ObservationIngestResult(false, 0, 0, "Endpoint response transform failed", transformError);

        if (!TryParseReadings(transformed, subjectId != null, out var readings, out var parseError))
            return new ObservationIngestResult(false, 0, 0, "Transformed output is not a valid reading array.", parseError);

        if (subjectId != null)
            return await ObserveOntoSubjectAsync(subjectId.Value, readings);

        // Things scale with entities, observations with readings — so group by entity name and
        // touch each entity's structure once.
        MyceliumClient.MyceliumThing? observedPredicate = null;
        var entitiesTouched = 0;
        var observationsSubmitted = 0;

        foreach (var group in readings.GroupBy(r => r.Name, StringComparer.Ordinal))
        {
            var name = group.Key;
            var entityReadings = group.ToList();

            var entity = await _myceliumClient.FindThingByNameAsync(name);
            var created = false;
            if (entity == null)
            {
                // First sighting: declare the entity and its observable properties (the first
                // reading seeds the shape), bound their retention, and link it to the source once.
                entity = await _myceliumClient.CreateThingAsync(name, entityReadings[0].Properties);
                if (entity == null)
                    return new ObservationIngestResult(false, entitiesTouched, observationsSubmitted,
                        "Failed to create entity thing.", name);
                created = true;

                foreach (var prop in entityReadings[0].Properties.Keys)
                    await _myceliumClient.SetPropertyModeAsync(entity.Value.Id, prop, DefaultObservationMode);

                observedPredicate ??= await _myceliumClient.FindThingByNameAsync("observed")
                                      ?? await _myceliumClient.CreateThingAsync("observed");
                if (observedPredicate == null)
                    return new ObservationIngestResult(false, entitiesTouched, observationsSubmitted,
                        "Failed to resolve or create 'observed' predicate.", null);

                var related = await _myceliumClient.CreateRelationshipAsync(
                    endpointThingId, observedPredicate.Value.Id, entity.Value.Id);
                if (!related)
                    return new ObservationIngestResult(false, entitiesTouched, observationsSubmitted,
                        "Failed to relate entity to endpoint.", name);
            }

            entitiesTouched++;

            // The reading consumed by creation is captured as each property's seed Fact; every
            // remaining reading becomes an observation on the entity's series.
            var toObserve = created ? entityReadings.Skip(1) : entityReadings;
            var samples = SamplesOf(toObserve);

            if (samples.Count > 0)
            {
                if (!await _myceliumClient.SubmitObservationsAsync(entity.Value.Id, samples))
                    return new ObservationIngestResult(false, entitiesTouched, observationsSubmitted,
                        "Failed to submit observations for entity.", name);
                observationsSubmitted += samples.Count;
            }
        }

        return new ObservationIngestResult(true, entitiesTouched, observationsSubmitted, null, null);
    }

    // Nothing is created here, so no `observed` edge is written either — the same as any fetch
    // landing on an entity that already existed, and the reason a value discovered this way cannot
    // yet be walked back to the source that produced it.
    private async Task<ObservationIngestResult> ObserveOntoSubjectAsync(Guid subjectId, List<Reading> readings)
    {
        var samples = SamplesOf(readings);

        // Nothing resolved, nothing created, nothing written — so nothing to report. This is
        // deliberately not what the name path answers: that one counts the entity it resolved even
        // when the reading gave it no values, because resolving is work this path never does.
        if (samples.Count == 0)
            return new ObservationIngestResult(true, 0, 0, null, null);

        if (!await _myceliumClient.SubmitObservationsAsync(subjectId, samples))
            return new ObservationIngestResult(false, 1, 0, "Failed to submit observations for entity.", null);

        return new ObservationIngestResult(true, 1, samples.Count, null, null);
    }

    private static List<ObservationSample> SamplesOf(IEnumerable<Reading> readings) =>
        readings
            .SelectMany(reading => reading.Properties.Select(
                value => new ObservationSample(value.Key, value.Value, reading.ObservedAt)))
            .ToList();

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

    private static bool TryParseReadings(
        string json, bool subjectSupplied, out List<Reading> readings, out string error)
    {
        readings = new List<Reading>();
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
                return TryParseReading(doc.RootElement, subjectSupplied, readings, out error);

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                error = "Transformed output must be a JSON object or array.";
                return false;
            }

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (!TryParseReading(element, subjectSupplied, readings, out error))
                    return false;
            }
        }

        return true;
    }

    private static bool TryParseReading(
        JsonElement element, bool subjectSupplied, List<Reading> readings, out string error)
    {
        error = string.Empty;

        if (element.ValueKind != JsonValueKind.Object)
        {
            error = "Each reading must be a JSON object.";
            return false;
        }

        // A call that names its subject has already said what the reading is about, so the
        // expression need not repeat it — and when it does, the caller's subject is the one used.
        var named = element.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String;
        if (!named && !subjectSupplied)
        {
            error = "Reading is missing a string 'name' property.";
            return false;
        }

        if (!element.TryGetProperty("properties", out var propsProp) || propsProp.ValueKind != JsonValueKind.Object)
        {
            error = "Reading is missing an object 'properties' property.";
            return false;
        }

        var props = JsonSerializer.Deserialize<Dictionary<string, object?>>(propsProp.GetRawText())
                    ?? new Dictionary<string, object?>();

        // Left null when the source names no time: Mycelium then stamps the batch from the model
        // clock, which a run can anchor away from real time. Stamping here would read this
        // process's wall clock instead, putting the sample on a timeline the model never writes to.
        DateTime? observedAt = null;
        if (element.TryGetProperty("observedAt", out var atProp) && atProp.ValueKind == JsonValueKind.String
            && DateTime.TryParse(atProp.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            observedAt = parsed;
        }

        var name = named ? nameProp.GetString() ?? string.Empty : string.Empty;
        readings.Add(new Reading(name, props, observedAt));
        return true;
    }

    private record Reading(string Name, Dictionary<string, object?> Properties, DateTime? ObservedAt);
}
