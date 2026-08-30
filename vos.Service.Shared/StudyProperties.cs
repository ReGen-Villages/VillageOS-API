using System.Net.Http.Json;
using System.Text.Json;

namespace vos.Service.Shared;

/// <summary>The two calls a compute service makes against Mycelium: read the study it was dispatched
/// against, and write a computed value back as a Fact — onto that study, or onto a Thing beside it whose
/// own result it is. Bound to the service making them, so every refusal names itself.
///
/// <para>Shared rather than copied into each handler: both calls sat in EnergyBalance and WaterReserve
/// and the write in LandAllocation, identical but for the name in the message, which is the shape where
/// one gets fixed and the others are left as they were. <see cref="StudyInputs"/> was shared out of the
/// same handlers for the same reason.</para>
///
/// <para>The client is supplied rather than built per call, unlike
/// <see cref="MyceliumClientBase.SetFactAsync"/>: a recompute writes several outputs and would otherwise
/// exchange a token for each one.</para></summary>
public sealed class StudyProperties(HttpClient client, string myceliumUrl, string serviceName)
{
    /// <summary>The study's effective properties, so an assumption declared on the shared archetype
    /// resolves without the service knowing where it came from.</summary>
    public async Task<StudyInputs> ReadAsync(Guid studyId, CancellationToken cancellationToken = default)
    {
        var response = await client.GetAsync(
            $"{myceliumUrl}{MyceliumRoutes.ThingProperties(studyId)}", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"{serviceName} could not read the study {studyId} ({(int)response.StatusCode} {response.StatusCode})");

        var properties = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        // An answer that holds no properties at all is a broken read, not a study whose inputs have yet to
        // arrive: waiting on it would hold the study back for ever with nothing saying why.
        if (properties.ValueKind != JsonValueKind.Object)
            throw new HttpRequestException(
                $"{serviceName} read the study {studyId} and was answered {properties.ValueKind} where its properties belong");

        return new StudyInputs(properties, serviceName);
    }

    public async Task WriteAsync(Guid thingId, string property, object value, CancellationToken cancellationToken = default)
    {
        var path = $"{myceliumUrl}{MyceliumRoutes.ThingPropertyFacts(thingId, property)}";
        var response = await client.PostAsJsonAsync(path, new { value }, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"{serviceName} could not write {thingId}.{property} ({(int)response.StatusCode} {response.StatusCode})");
    }
}
