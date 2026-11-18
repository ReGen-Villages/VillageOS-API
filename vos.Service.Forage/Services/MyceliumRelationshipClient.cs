using System.Net;
using System.Text.Json;
using vos.Service.Shared;

namespace vos.Service.Forage.Services;

// The writes this service makes to the model: relating a subject to the vocabulary member a fetched word names — with the stale relationship
// removed — is how a discovered word becomes a relationship; relating a site to the Place its code names; and the
// coverage Things a run records what each call came to on.
//
// Every relationship is written as a fragment because the engine skips a relationship it already holds and passes over a removal
// already made, so a repeated write succeeds and leaves one copy.
public sealed class MyceliumRelationshipClient : MyceliumClientBase, ICoverageWriter
{
    public MyceliumRelationshipClient(
        IHttpClientFactory httpClientFactory, ILogger<MyceliumRelationshipClient> logger,
        string myceliumUrl, LaunchToken? launchToken = null, string? apiKey = null)
        : base(httpClientFactory, logger, myceliumUrl, launchToken, apiKey: apiKey) { }

    public async Task<bool> WriteCoverageAsync(
        Guid coverageId, string name, IReadOnlyList<(Guid Predicate, Guid Target)> relationships, CancellationToken cancellationToken) =>
        (await PostFragmentAsync(name, new
        {
            name,
            things = new[] { new { id = coverageId, name } },
            relationships = relationships.Select(relationship => new
            {
                subject = coverageId,
                predicate = relationship.Predicate,
                target = relationship.Target,
            }),
        }, cancellationToken)).Answer == WriteAnswer.Written;

    // Each replaced relationship is removed in the same write, so the subject never ends with both or with neither.
    public Task<WriteOutcome> WriteRelationshipAsync(
        string name, Guid subjectId, Guid predicateId, Guid targetId, IReadOnlyList<Guid> replacedTargetIds,
        CancellationToken cancellationToken) =>
        PostFragmentAsync(name, new
        {
            name,
            relationships = new[] { new { subject = subjectId, predicate = predicateId, target = targetId } },
            retractions = replacedTargetIds.Select(replaced => new { subject = subjectId, predicate = predicateId, target = replaced }),
        }, cancellationToken);

    public async Task<bool> RelateAsync(
        Guid subjectId, Guid predicateId, Guid targetId, CancellationToken cancellationToken) =>
        (await WriteRelationshipAsync($"{subjectId} -{predicateId}-> {targetId}", subjectId, predicateId, targetId, [], cancellationToken))
        .Answer == WriteAnswer.Written;

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

    private async Task<WriteOutcome> PostFragmentAsync(string name, object fragment, CancellationToken cancellationToken)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(30));
            var response = await client.PostAsJsonAsync($"{MyceliumUrl}/api/model/fragment", fragment, cancellationToken);
            if (response.IsSuccessStatusCode) return new WriteOutcome(WriteAnswer.Written);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            Logger.LogWarning("Refused writing {Name}: {Status} {Reason}", name, response.StatusCode, body);
            var reason = ErrorIn(body) ?? $"{(int)response.StatusCode}: {body}";
            return response.StatusCode switch
            {
                HttpStatusCode.Forbidden => new WriteOutcome(WriteAnswer.Refused, reason),
                HttpStatusCode.BadRequest => new WriteOutcome(WriteAnswer.Rejected, reason),
                _ => new WriteOutcome(WriteAnswer.Unanswered, reason),
            };
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Error writing {Name}", name);
            return new WriteOutcome(WriteAnswer.Unanswered, exception.Message);
        }
    }

    private static string? ErrorIn(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
                ? error.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

// Refused is a 403, which no retry passes; Rejected is a 400, whose cause only the model can tell; Unanswered is
// anything a later try may pass.
public enum WriteAnswer { Written, Refused, Rejected, Unanswered }

public sealed record WriteOutcome(WriteAnswer Answer, string? Reason = null);
