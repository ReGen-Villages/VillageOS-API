using Microsoft.Extensions.Logging;
using vos.Service.Forage.Helpers;

namespace vos.Service.Forage.Services;

// Runs one site against every source covering it: fetch each of a source's calls through the
// configured fetcher, bounded in flight, and report both halves.
//
// A call that fails leaves its value undiscovered and does not stop the others. Public data
// portals go down, and an intake that aborted because one provider was unavailable would be
// abandoned — so a failure is an outcome the report carries rather than an exception that ends the
// run.
public sealed class DiscoveryRunner
{
    private readonly ISourceFetcher _fetcher;
    private readonly int _maxConcurrentSources;
    private readonly ILogger<DiscoveryRunner> _logger;

    public DiscoveryRunner(ISourceFetcher fetcher, int maxConcurrentSources, ILogger<DiscoveryRunner> logger)
    {
        _fetcher = fetcher;
        _maxConcurrentSources = maxConcurrentSources;
        _logger = logger;
    }

    public async Task<DiscoveryReport> RunAsync(
        Guid siteId,
        IReadOnlyList<CoveringSource> covering,
        CancellationToken cancellationToken)
    {
        // The bound holds across every call, not per source: a source called once per assessment
        // must not widen the burst the bound exists to keep polite.
        var calls = covering
            .SelectMany(source => source.Calls.Select(call => (Source: source, Call: call)))
            .ToList();
        var outcomes = new SourceOutcome[calls.Count];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, calls.Count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _maxConcurrentSources,
                CancellationToken = cancellationToken,
            },
            async (index, token) =>
            {
                var (source, call) = calls[index];
                var outcome = await FetchOneAsync(source, call, token)
                    with { SubjectId = call.SubjectId, SourceId = source.SourceId };
                outcomes[index] = call.SubjectId == siteId
                    ? outcome
                    : outcome with { Subject = call.SubjectName };
            });

        var resolved = outcomes.Where(outcome => outcome.Resolved).ToList();
        var unresolved = outcomes.Where(outcome => !outcome.Resolved).ToList();

        _logger.LogInformation(
            "Discovery for site {SiteId}: {Resolved} calls resolved, {Unresolved} unresolved",
            siteId, resolved.Count, unresolved.Count);

        return new DiscoveryReport(resolved, unresolved);
    }

    // The fetcher reports a failed source rather than throwing, but it reaches the network and a
    // caller of this run must not be handed an exception raised on one source's behalf. Catching
    // here is what keeps one provider from ending the run for every other.
    private async Task<SourceOutcome> FetchOneAsync(
        CoveringSource source,
        SourceCall call,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _fetcher.FetchAsync(
                call.SubjectId, source.Name, source.EndpointName, call.Values, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Source {Source} failed to resolve", source.Name);
            return new SourceOutcome(source.Name, false, exception.Message);
        }
    }
}
