using System.Net.Http.Json;
using System.Text.Json;
using vos.Service.Shared;

namespace vos.Service.WaterReserve.Services;

// The reactive (model-driven) form of the water analysis (User Story #5839). It reacts to a graph relationship whose
// subject is the SiteStudy: reads its inputs straight off the study's effective properties, computes with
// WaterReserveCalculator, and writes its outputs back onto the study as Facts — so the study's
// WaterResilient range re-evaluates. No pipeline, no wires: the compute is a value on the study.
public sealed class WaterReserveReactiveHandler : MyceliumClientBase
{
    public WaterReserveReactiveHandler(
        IHttpClientFactory httpClientFactory, ILogger<WaterReserveReactiveHandler> logger,
        string myceliumUrl, string? serviceToken = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken)
    {
    }

    // Input port names, read off the study by name.
    private static readonly string[] Inputs = { "population", "perCapitaConsumptionM3", "storageCapacityM3" };

    // The same names as a set, for the subscription that recomputes when one moves. Derived from Inputs
    // rather than restated, so the filter cannot come to disagree with what Compute reads.
    public static readonly IReadOnlySet<string> InputProperties = new HashSet<string>(Inputs, StringComparer.Ordinal);

    // Read the study's inputs, compute, and write the outputs back onto it. Returns the outputs.
    public async Task<WaterReserveOutputs> RecomputeAsync(Guid studyId, CancellationToken cancellationToken = default)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10));

        var props = await FetchEffectivePropertiesAsync(client, studyId, cancellationToken);
        var inputs = new StudyInputs(props, "WaterReserve");
        var result = WaterReserveCalculator.Compute(new WaterReserveInputs(
            inputs.Number(Inputs[0]), inputs.Number(Inputs[1]), inputs.Number(Inputs[2])));

        await WriteAsync(client, studyId, "daysOfSupply", result.DaysOfSupply, cancellationToken);
        await WriteAsync(client, studyId, "emergencyReserveM3", result.EmergencyReserveM3, cancellationToken);
        await WriteAsync(client, studyId, "annualConsumptionM3", result.AnnualConsumptionM3, cancellationToken);
        await WriteAsync(client, studyId, "pctAnnualConsumption", result.PctAnnualConsumption, cancellationToken);
        return result;
    }

    private async Task<JsonElement> FetchEffectivePropertiesAsync(HttpClient client, Guid thingId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync($"{MyceliumUrl}{MyceliumRoutes.ThingProperties(thingId)}", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"WaterReserve could not read the study {thingId} ({(int)response.StatusCode} {response.StatusCode})");
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    private async Task WriteAsync(HttpClient client, Guid thingId, string property, object value, CancellationToken cancellationToken)
    {
        var path = $"{MyceliumUrl}/api/things/{thingId}/properties/{Uri.EscapeDataString(property)}/facts";
        var response = await client.PostAsJsonAsync(path, new { value }, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"WaterReserve could not write {thingId}.{property} ({(int)response.StatusCode} {response.StatusCode})");
    }

}
