using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using vos.Service.Shared;

namespace vos.Service.Forage.Services;

// The writes this service makes to the model: relating a study to a compute service starts the
// analysis; relating a subject to the vocabulary member a fetched word names — with the stale relationship
// removed — is how a discovered word becomes a relationship; and the coverage Things a run records
// what each call came to on. Separate from the fetcher, which speaks to endpoint services rather than
// to the model.
public sealed class MyceliumRelationshipClient : MyceliumClientBase, ICoverageWriter
{
    public MyceliumRelationshipClient(
        IHttpClientFactory httpClientFactory, ILogger<MyceliumRelationshipClient> logger,
        string myceliumUrl, LaunchToken? launchToken = null, string? apiKey = null)
        : base(httpClientFactory, logger, myceliumUrl, launchToken, apiKey: apiKey) { }

    public async Task<bool> WriteCoverageAsync(
        Guid coverageId, string name, IReadOnlyList<(Guid Predicate, Guid Target)> relationships, CancellationToken cancellationToken)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(30));
            var response = await client.PostAsJsonAsync($"{MyceliumUrl}/api/model/fragment", new
            {
                name,
                things = new[] { new { id = coverageId, name } },
                relationships = relationships.Select(relationship => new
                {
                    subject = coverageId,
                    predicate = relationship.Predicate,
                    target = relationship.Target,
                }),
            }, cancellationToken);
            if (response.IsSuccessStatusCode) return true;

            Logger.LogWarning("Refused writing {Name}: {Status} {Reason}", name, response.StatusCode,
                await response.Content.ReadAsStringAsync(cancellationToken));
            return false;
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Error writing {Name}", name);
            return false;
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
            if (response.IsSuccessStatusCode)
                return true;

            Logger.LogWarning("Refused relating {Subject} to {Target}: {Status} {Reason}", subjectId, targetId,
                response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
            return false;
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
