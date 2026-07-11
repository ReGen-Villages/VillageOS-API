using System.Net.Http.Json;
using System.Text.Json;
using vos.ManagedMicroservice.Shared;

namespace vos.ManagedMicroservice.WaterReserve.Services;

/// <summary>
/// The reactive (model-driven) form of the water analysis (User Story #5839). It reacts to a graph relationship whose
/// subject is the site anchor: reads its inputs straight off the anchor's effective properties, computes with
/// <see cref="WaterReserveCalculator"/>, and writes its outputs back onto the anchor as Facts — so the anchor's
/// <c>WaterResilient</c> range re-evaluates. No pipeline, no wires: the compute is a value on the anchor.
/// </summary>
public sealed class WaterReserveReactiveHandler : MyceliumClientBase
{
    public WaterReserveReactiveHandler(
        IHttpClientFactory httpClientFactory, ILogger<WaterReserveReactiveHandler> logger,
        string myceliumUrl, string? serviceToken = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken)
    {
    }

    // Input port names, read off the anchor by name.
    private static readonly string[] Inputs = { "population", "perCapitaConsumptionM3", "storageCapacityM3" };

    /// <summary>Read the anchor's inputs, compute, and write the outputs back onto it. Returns the outputs.</summary>
    public async Task<WaterReserveOutputs> RecomputeAsync(Guid anchorId, CancellationToken cancellationToken = default)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10));

        var props = await FetchEffectivePropertiesAsync(client, anchorId, cancellationToken);
        var result = WaterReserveCalculator.Compute(new WaterReserveInputs(
            Number(props, Inputs[0]), Number(props, Inputs[1]), Number(props, Inputs[2])));

        await WriteAsync(client, anchorId, "daysOfSupply", result.DaysOfSupply, cancellationToken);
        await WriteAsync(client, anchorId, "emergencyReserveM3", result.EmergencyReserveM3, cancellationToken);
        await WriteAsync(client, anchorId, "annualConsumptionM3", result.AnnualConsumptionM3, cancellationToken);
        await WriteAsync(client, anchorId, "pctAnnualConsumption", result.PctAnnualConsumption, cancellationToken);
        return result;
    }

    private async Task<JsonElement> FetchEffectivePropertiesAsync(HttpClient client, Guid thingId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync($"{MyceliumUrl}/api/things/{thingId}/effective-properties", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"WaterReserve could not read the anchor {thingId} ({(int)response.StatusCode} {response.StatusCode})");
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    private async Task WriteAsync(HttpClient client, Guid thingId, string property, object value, CancellationToken cancellationToken)
    {
        var path = $"{MyceliumUrl}/api/things/{thingId}/properties/{Uri.EscapeDataString(property)}/facts";
        var response = await client.PostAsJsonAsync(path, new { value }, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"WaterReserve could not write {thingId}.{property} ({(int)response.StatusCode} {response.StatusCode})");
    }

    private static double Number(JsonElement props, string name)
    {
        if (props.ValueKind == JsonValueKind.Object)
            foreach (var p in props.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                    return ExtractDouble(p.Value);
        throw new KeyNotFoundException($"WaterReserve input '{name}' is not on the anchor.");
    }

    // effective-properties returns each property as { "Value": <v>, ... } (case-insensitive key).
    private static double ExtractDouble(JsonElement envelope)
    {
        var value = envelope;
        if (envelope.ValueKind == JsonValueKind.Object)
            foreach (var field in envelope.EnumerateObject())
                if (string.Equals(field.Name, "Value", StringComparison.OrdinalIgnoreCase)) { value = field.Value; break; }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), out var d) => d,
            _ => throw new InvalidOperationException($"WaterReserve input value is not numeric: {value.ValueKind}"),
        };
    }
}
