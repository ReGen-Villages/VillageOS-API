using Microsoft.Extensions.Logging;
using vos.Service.Confluence.Helpers;

namespace vos.Service.Confluence.Services;

// Starts the analysis for a site by relating its study to each compute service.
//
// Confluence calls no compute service. A connection bound to a service is a handled predicate, so
// creating `study -connection-> prototype` is what dispatches it — the model carries the trigger, and a
// second way to start an analysis would be a second thing to keep in step with it. It also means the
// analysis is a fact in the model rather than a call that happened, so what started one is answerable
// afterwards.
//
// The service reads its inputs off the study, writes its outputs back, and starts watching the study, so
// this edge is written once: every later change to an input recomputes without anything calling again.
public sealed class AnalysisSpawner
{
    private readonly MyceliumRelationshipClient _mycelium;
    private readonly ILogger<AnalysisSpawner> _logger;

    public AnalysisSpawner(MyceliumRelationshipClient mycelium, ILogger<AnalysisSpawner> logger)
    {
        _mycelium = mycelium;
        _logger = logger;
    }

    public async Task<AnalysisSpawn> SpawnAsync(
        Guid siteId, SiteAnalysis? analysis, CancellationToken cancellationToken)
    {
        if (analysis == null)
            return new AnalysisSpawn(false, "The site has no study to analyse.");

        if (analysis.Triggers.Count == 0)
            return new AnalysisSpawn(false, "The model marks no connection as one a site analysis starts.");

        var unstarted = new List<string>();
        foreach (var trigger in analysis.Triggers)
        {
            if (await _mycelium.CreateRelationshipAsync(
                    analysis.StudyId, trigger.ConnectionId, trigger.ServicePrototypeId, cancellationToken))
                continue;

            _logger.LogError("Could not start {Connection} for study {StudyId} of site {SiteId}.",
                trigger.ConnectionName, analysis.StudyId, siteId);
            unstarted.Add(trigger.ConnectionName);
        }

        // Partial failure is reported as failure while naming what did start, for the reason an
        // unresolved source carries one: a planner reading a balance has to know it is not there because
        // it could not be written, rather than because the figure is genuinely unknown.
        if (unstarted.Count > 0)
            return new AnalysisSpawn(false, "Could not start: " + string.Join(", ", unstarted) + ".");

        _logger.LogInformation("Analysis started for study {StudyId} of site {SiteId} on {Count} services.",
            analysis.StudyId, siteId, analysis.Triggers.Count);
        return new AnalysisSpawn(true, null);
    }
}

// Whether the analysis was started, and why not when it was not. A reason is always carried for the
// same reason an unresolved source carries one: "did not start" alone is unactionable.
public sealed record AnalysisSpawn(bool Started, string? Reason);
