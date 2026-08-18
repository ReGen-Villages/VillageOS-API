using Microsoft.Extensions.Logging;
using vos.Service.Confluence.Helpers;

namespace vos.Service.Confluence.Services;

// Runs one site against every source covering it: fetch each through the configured fetcher, bounded
// in flight, and report both halves.
//
// A source that fails leaves its value undiscovered and does not stop the others. Public data
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
        IReadOnlyDictionary<string, string> addressParameters,
        CancellationToken cancellationToken)
    {
        var outcomes = new SourceOutcome[covering.Count];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, covering.Count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _maxConcurrentSources,
                CancellationToken = cancellationToken,
            },
            async (index, token) =>
            {
                var source = covering[index];
                outcomes[index] = await FetchOneAsync(source, addressParameters, token);
            });

        var resolved = outcomes.Where(outcome => outcome.Resolved).ToList();
        var unresolved = outcomes.Where(outcome => !outcome.Resolved).ToList();

        _logger.LogInformation(
            "Discovery for site {SiteId}: {Resolved} resolved, {Unresolved} unresolved",
            siteId, resolved.Count, unresolved.Count);

        return new DiscoveryReport(siteId, resolved, unresolved);
    }

    // The fetcher reports a failed source rather than throwing, but it reaches the network and a
    // caller of this run must not be handed an exception raised on one source's behalf. Catching
    // here is what keeps one provider from ending the run for every other.
    private async Task<SourceOutcome> FetchOneAsync(
        CoveringSource source,
        IReadOnlyDictionary<string, string> addressParameters,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _fetcher.FetchAsync(
                source.Name, source.EndpointName, addressParameters, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Source {Source} failed to resolve", source.Name);
            return new SourceOutcome(source.Name, false, exception.Message);
        }
    }
}
