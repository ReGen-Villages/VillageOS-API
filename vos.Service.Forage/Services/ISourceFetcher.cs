namespace vos.Service.Forage.Services;

// One outbound fetch, as the run sees it. Never throws for a source that failed: a failure is an
// outcome the report carries, not something that ends the run for the sources beside it.
public interface ISourceFetcher
{
    Task<SourceOutcome> FetchAsync(
        Guid subjectId,
        string sourceName,
        string endpointName,
        IReadOnlyDictionary<string, string> addressParameters,
        CancellationToken cancellationToken);
}
