using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using vos.Service.Intake.Models;
using vos.Service.Shared;

namespace vos.Service.Intake.Services;

public sealed class IntakeMyceliumClient(
    IHttpClientFactory httpClientFactory,
    ILogger<IntakeMyceliumClient> logger,
    string myceliumUrl,
    string? serviceToken,
    string? apiKey = null)
    : MyceliumClientBase(httpClientFactory, logger, myceliumUrl, serviceToken, apiKey: apiKey)
{
    public async Task<Guid?> FindThingIdByNameAsync(string name, CancellationToken cancellation)
    {
        var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"{MyceliumUrl}/api/things?name={Uri.EscapeDataString(name)}", cancellation);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Looking up '{name}' failed ({(int)response.StatusCode} {response.StatusCode}).", null, response.StatusCode);

        var found = await response.Content.ReadFromJsonAsync<JsonElement>(cancellation);
        return found.ValueKind == JsonValueKind.Object
               && found.TryGetProperty("Id", out var identifier)
               && identifier.TryGetGuid(out var value)
            ? value
            : null;
    }

    /// <summary>Whether the model already holds the Thing under this identifier. Asked by identifier rather
    /// than by name because a name can answer with more than one Thing, and the caller derived this one.
    /// </summary>
    public async Task<bool> HoldsThingAsync(Guid identifier, CancellationToken cancellation)
    {
        var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"{MyceliumUrl}/api/things/{identifier}", cancellation);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return false;
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Looking up '{identifier}' failed ({(int)response.StatusCode} {response.StatusCode}).",
                null, response.StatusCode);

        return true;
    }

    public async Task ApplyFragmentAsync(ModelFragment fragment, CancellationToken cancellation)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(30));
        var document = JsonSerializer.Serialize(fragment);
        var response = await client.PostAsync(
            $"{MyceliumUrl}/api/model/fragment",
            new StringContent(document, Encoding.UTF8, "application/json"),
            cancellation);

        if (response.IsSuccessStatusCode)
            return;

        // The model's refusal names the Thing and the property it could not accept; a bare status code
        // throws that away and leaves the planner with nothing to correct.
        var refusal = await response.Content.ReadAsStringAsync(cancellation);
        throw new SubmissionError($"The model refused the submission: {refusal}");
    }
}
