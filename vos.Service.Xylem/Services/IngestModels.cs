namespace vos.Service.Xylem.Services;

// How an ingest applies to the model: merge into the current model (idempotent upsert by
// stable id) or replace it with a brand-new model built from the IFC.
public enum IngestMode
{
    Merge,
    NewModel,
}

// A count is null when the run succeeded and the service could not read it off what the tool printed.
// Nought would say the model gained nothing, which a caller cannot tell from a count nobody read.
public record IngestRunResult(
    bool Success,
    int? ThingsCreated,
    int? ThingsUpdated,
    int? RelationshipsCreated,
    string? Error);

public record IngestResult(
    bool Success,
    int? ThingsCreated,
    int? ThingsUpdated,
    int? RelationshipsCreated,
    string? Error)
{
    public static IngestResult Ok(int? created, int? updated, int? relsCreated) =>
        new(true, created, updated, relsCreated, null);

    public static IngestResult Failed(string error) => new(false, 0, 0, 0, error);
}
