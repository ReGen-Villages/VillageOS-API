namespace vos.Service.Forage.Services;

// An interface so what the ledger decides can be tested without a gateway in the way.
public interface ICoverageWriter
{
    // The record and its relationships go in one write, so a record is never left reaching nothing. False where the
    // model refused it: nothing was recorded at all, so the call stays outstanding.
    Task<bool> WriteCoverageAsync(Guid coverageId, string name, IReadOnlyList<(Guid Predicate, Guid Target)> relationships,
        CancellationToken cancellationToken);

    Task<bool> RelateAsync(Guid subjectId, Guid predicateId, Guid targetId, CancellationToken cancellationToken);

    Task<bool> WriteFactAsync(Guid thingId, string property, object? value, CancellationToken cancellationToken);
}
