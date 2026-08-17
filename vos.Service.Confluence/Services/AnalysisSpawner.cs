using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using vos.Service.Shared;

namespace vos.Service.Confluence.Services;

// Starts the analysis for a site by writing `Site runs Pipeline`.
//
// Confluence never calls the orchestrator. `runs` is a handled predicate, so creating the edge is
// what dispatches the pipeline — the model carries the trigger, and a second way to start an
// analysis would be a second thing to keep in step with it. It also means the run is a fact in the
// model rather than a call that happened, so what started an analysis is answerable afterwards.
public sealed class AnalysisSpawner
{
    public const string RunsPredicate = "runs";

    private readonly MyceliumRelationshipClient _mycelium;
    private readonly ILogger<AnalysisSpawner> _logger;

    public AnalysisSpawner(MyceliumRelationshipClient mycelium, ILogger<AnalysisSpawner> logger)
    {
        _mycelium = mycelium;
        _logger = logger;
    }

    // Null pipeline means the site names none: nothing was ever going to run, so there is nothing to
    // report as failed. Discovery has already written its observations either way.
    public async Task<AnalysisSpawn> SpawnAsync(Guid siteId, Guid? pipelineId, CancellationToken cancellationToken)
    {
        if (pipelineId == null)
            return new AnalysisSpawn(false, "The site is analysed by no pipeline.");

        var runs = await _mycelium.FindPredicateAsync(RunsPredicate, cancellationToken);
        if (runs == null)
        {
            _logger.LogError("Cannot start the analysis for site {SiteId}: the model has no '{Predicate}' predicate.",
                siteId, RunsPredicate);
            return new AnalysisSpawn(false, $"The model has no '{RunsPredicate}' predicate.");
        }

        if (!await _mycelium.CreateRelationshipAsync(siteId, runs.Value, pipelineId.Value, cancellationToken))
        {
            _logger.LogError("Failed to start the analysis for site {SiteId} on pipeline {PipelineId}.",
                siteId, pipelineId);
            return new AnalysisSpawn(false, "Writing the run relationship failed.");
        }

        _logger.LogInformation("Analysis started for site {SiteId} on pipeline {PipelineId}.", siteId, pipelineId);
        return new AnalysisSpawn(true, null);
    }
}

// Whether the analysis was started, and why not when it was not. A reason is always carried for the
// same reason an unresolved source carries one: "did not start" alone is unactionable.
public sealed record AnalysisSpawn(bool Started, string? Reason);

// The two writes starting an analysis needs. Separate from the fetcher, which speaks to endpoint
// services rather than to the model.
public sealed class MyceliumRelationshipClient : MyceliumClientBase
{
    public MyceliumRelationshipClient(
        IHttpClientFactory httpClientFactory, ILogger<MyceliumRelationshipClient> logger,
        string myceliumUrl, string? serviceToken = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken) { }

    public async Task<Guid?> FindPredicateAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10));
            var response = await client.GetAsync(
                $"{MyceliumUrl}/api/things?name={Uri.EscapeDataString(name)}", cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            foreach (var property in root.EnumerateObject())
                if (string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase)
                    && Guid.TryParse(property.Value.GetString(), out var id))
                    return id;

            return null;
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Error finding the '{Name}' predicate", name);
            return null;
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
}
