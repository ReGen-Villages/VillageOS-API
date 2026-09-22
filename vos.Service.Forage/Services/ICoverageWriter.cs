namespace vos.Service.Forage.Services;

// The model writes a run makes for itself: minting a coverage, relating it, and recording what its
// call came to. Separate from the fetcher, which speaks to endpoint services rather than to the model,
// and an interface for the reason ISourceFetcher is one — what the ledger decides is worth testing
// without a gateway in the way.
public interface ICoverageWriter
{
    // The new Thing's identifier, or null where the model refused it. A refusal is not a coverage that
    // resolved to nothing: nothing was recorded at all, so the call stays outstanding.
    Task<Guid?> MintAsync(string name, CancellationToken cancellationToken);

    Task<bool> RelateAsync(Guid subjectId, Guid predicateId, Guid targetId, CancellationToken cancellationToken);

    Task<bool> WriteFactAsync(Guid thingId, string property, object? value, CancellationToken cancellationToken);
}
