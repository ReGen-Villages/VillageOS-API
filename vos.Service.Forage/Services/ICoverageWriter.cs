namespace vos.Service.Forage.Services;

// The model writes a run makes for itself: minting a coverage, relating it, and recording what its
// call came to. Separate from the fetcher, which speaks to endpoint services rather than to the model,
// and an interface for the reason ISourceFetcher is one — what the ledger decides is worth testing
// without a gateway in the way.
public interface ICoverageWriter
{
    // The record and its relationships go in one write, so a record is never left reaching nothing. False where the
    // model refused it: nothing was recorded at all, so the call stays outstanding.
    Task<bool> WriteCoverageAsync(Guid coverageId, string name, IReadOnlyList<(Guid Predicate, Guid Target)> relationships,
        CancellationToken cancellationToken);

    Task<bool> RelateAsync(Guid subjectId, Guid predicateId, Guid targetId, CancellationToken cancellationToken);

    Task<bool> WriteFactAsync(Guid thingId, string property, object? value, CancellationToken cancellationToken);
}
