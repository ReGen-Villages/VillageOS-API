using System.Text.Json;
using vos.Service.Shared;

namespace vos.Service.Forage.Services;

// The writes this service makes to the model: relating a subject to the vocabulary member a fetched word names — with the stale relationship
// removed — is how a discovered word becomes a relationship; relating a site to the Place its code names; and the
// coverage Things a run records what each call came to on. Separate from the fetcher, which speaks to endpoint
// services rather than to the model.
//
// Every relationship is written as a fragment because the engine skips a relationship it already holds and passes over a removal
// already made, so a repeated write succeeds and leaves one copy.
public sealed class MyceliumRelationshipClient : MyceliumClientBase, ICoverageWriter
{
    public MyceliumRelationshipClient(
        IHttpClientFactory httpClientFactory, ILogger<MyceliumRelationshipClient> logger,
        string myceliumUrl, LaunchToken? launchToken = null, string? apiKey = null)
        : base(httpClientFactory, logger, myceliumUrl, launchToken, apiKey: apiKey) { }

    public Task<bool> WriteCoverageAsync(
        Guid coverageId, string name, IReadOnlyList<(Guid Predicate, Guid Target)> relationships, CancellationToken cancellationToken) =>
        PostFragmentAsync(name, new
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

    // Each replaced relationship is removed in the same write, so the subject never ends with both or with neither.
    public Task<bool> WriteRelationshipAsync(
        string name, Guid subjectId, Guid predicateId, Guid targetId, IReadOnlyList<Guid> replacedTargetIds,
        CancellationToken cancellationToken) =>
        PostFragmentAsync(name, new
        {
            name,
            relationships = new[] { new { subject = subjectId, predicate = predicateId, target = targetId } },
            retractions = replacedTargetIds.Select(replaced => new { subject = subjectId, predicate = predicateId, target = replaced }),
        }, cancellationToken);

    public Task<bool> RelateAsync(
        Guid subjectId, Guid predicateId, Guid targetId, CancellationToken cancellationToken) =>
        WriteRelationshipAsync($"{subjectId} -{predicateId}-> {targetId}", subjectId, predicateId, targetId, [], cancellationToken);

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

    private async Task<bool> PostFragmentAsync(string name, object fragment, CancellationToken cancellationToken)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(30));
            var response = await client.PostAsJsonAsync($"{MyceliumUrl}/api/model/fragment", fragment, cancellationToken);
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
}
