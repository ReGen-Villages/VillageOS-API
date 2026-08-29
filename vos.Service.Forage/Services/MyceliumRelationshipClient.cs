using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using vos.Service.Shared;

namespace vos.Service.Forage.Services;

// The writes this service makes to the model: relating a study to a compute service starts the
// analysis; relating a subject to the vocabulary member a fetched word names — with the stale edge
// removed — is how a discovered word becomes an edge (#6809); and the coverage Things a run records
// what each call came to on. Separate from the fetcher, which speaks to endpoint services rather than
// to the model.
public sealed class MyceliumRelationshipClient : MyceliumClientBase, ICoverageWriter
{
    public MyceliumRelationshipClient(
        IHttpClientFactory httpClientFactory, ILogger<MyceliumRelationshipClient> logger,
        string myceliumUrl, string? serviceToken = null, string? apiKey = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken, apiKey: apiKey) { }

    public async Task<Guid?> MintAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(30));
            var response = await client.PostAsJsonAsync(
                $"{MyceliumUrl}/api/things", new { Name = name }, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Logger.LogWarning("Failed to mint {Name}: {Status}", name, response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            if (TryGetPropertyCaseInsensitive(body, "Id", out var id) && id.TryGetGuid(out var minted))
                return minted;

            // Worse than a refusal, and so said separately: the create worked, so the Thing is in the
            // model with nothing able to reach it, and the next attempt adds another beside it.
            Logger.LogWarning("Minted {Name} and the model's answer named no identifier", name);
            return null;
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Error minting {Name}", name);
            return null;
        }
    }

    public Task<bool> RelateAsync(
        Guid subjectId, Guid predicateId, Guid targetId, CancellationToken cancellationToken)
        => CreateRelationshipAsync(subjectId, predicateId, targetId, cancellationToken);

    // A Fact rather than an Observation: a run asserts what its own call came to, and nothing samples it
    // from a provider. The properties are declared FactOnly, so an observation would be refused.
    public async Task<bool> WriteFactAsync(
        Guid thingId, string property, object? value, CancellationToken cancellationToken)
    {
        try
        {
            await SetFactAsync(thingId, property, value);
            return true;
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Error writing {Property} onto {Thing}", property, thingId);
            return false;
        }
    }

    public async Task<bool> CreateRelationshipAsync(
        Guid subjectId, Guid predicateId, Guid targetId, CancellationToken cancellationToken)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(30));
            var response = await client.PostAsJsonAsync(
                $"{MyceliumUrl}/api/relationships",
                new { subjectId, predicateId, targetId },
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Error relating {Subject} to {Target}", subjectId, targetId);
            return false;
        }
    }

    public async Task<bool> DeleteRelationshipAsync(Guid relationshipId, CancellationToken cancellationToken)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(30));
            var response = await client.DeleteAsync(
                $"{MyceliumUrl}/api/relationships/{relationshipId}", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Error removing relationship {RelationshipId}", relationshipId);
            return false;
        }
    }
}
