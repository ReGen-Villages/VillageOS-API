using System.Net.Http.Json;
using System.Text.Json;
using vos.ManagedMicroservice.Shared;

namespace vos.ManagedMicroservice.EnergyBalance.Services;

// The reactive (model-driven) form of the energy analysis (User Story #5839). Instead of running as a DAG node with
// wired ports, it reacts to a graph relationship whose subject is the SiteStudy: it reads its inputs straight off
// the study's effective properties, computes with EnergyBalanceCalculator, and writes its outputs back
// onto the study as Facts — so the study's judge ranges (e.g. EnergyNetPositive) re-evaluate. No pipeline, no
// wires: the compute is a value on the study, like a roll-up or a range.
public sealed class EnergyBalanceReactiveHandler : MyceliumClientBase
{
    public EnergyBalanceReactiveHandler(
        IHttpClientFactory httpClientFactory, ILogger<EnergyBalanceReactiveHandler> logger,
        string myceliumUrl, string? serviceToken = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken)
    {
    }

    // Input/output port names, read from / written to the study by name.
    private static readonly string[] Inputs =
    {
        "solarPvAreaM2", "solarResourceKwhPerM2PerYear", "pvEfficiency",
        "otherGenerationMwhPerYear", "annualConsumptionMwhPerYear",
    };

    // Read the study's inputs, compute, and write the outputs back onto it. Returns the outputs.
    public async Task<EnergyBalanceOutputs> RecomputeAsync(Guid studyId, CancellationToken cancellationToken = default)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10));

        var props = await FetchEffectivePropertiesAsync(client, studyId, cancellationToken);
        var result = EnergyBalanceCalculator.Compute(new EnergyBalanceInputs(
            Number(props, Inputs[0]), Number(props, Inputs[1]), Number(props, Inputs[2]),
            Number(props, Inputs[3]), Number(props, Inputs[4])));

        await WriteAsync(client, studyId, "pctOfConsumption", result.PctOfConsumption, cancellationToken);
        await WriteAsync(client, studyId, "netPositive", result.NetPositive, cancellationToken);
        await WriteAsync(client, studyId, "solarGenerationMwhPerYear", result.SolarGenerationMwhPerYear, cancellationToken);
        await WriteAsync(client, studyId, "totalGenerationMwhPerYear", result.TotalGenerationMwhPerYear, cancellationToken);
        return result;
    }

    private async Task<JsonElement> FetchEffectivePropertiesAsync(HttpClient client, Guid thingId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync($"{MyceliumUrl}/api/things/{thingId}/effective-properties", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"EnergyBalance could not read the study {thingId} ({(int)response.StatusCode} {response.StatusCode})");
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    private async Task WriteAsync(HttpClient client, Guid thingId, string property, object value, CancellationToken cancellationToken)
    {
        var path = $"{MyceliumUrl}/api/things/{thingId}/properties/{Uri.EscapeDataString(property)}/facts";
        var response = await client.PostAsJsonAsync(path, new { value }, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"EnergyBalance could not write {thingId}.{property} ({(int)response.StatusCode} {response.StatusCode})");
    }

    private static double Number(JsonElement props, string name)
    {
        if (props.ValueKind == JsonValueKind.Object)
            foreach (var p in props.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                    return ExtractDouble(p.Value);
        throw new KeyNotFoundException($"EnergyBalance input '{name}' is not on the study.");
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
            _ => throw new InvalidOperationException($"EnergyBalance input value is not numeric: {value.ValueKind}"),
        };
    }
}
