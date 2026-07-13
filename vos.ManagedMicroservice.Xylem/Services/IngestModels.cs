namespace vos.ManagedMicroservice.Xylem.Services;

// How an ingest applies to the model: merge into the current model (idempotent upsert by
// stable id) or replace it with a brand-new model built from the IFC.
public enum IngestMode
{
    Merge,
    NewModel,
}

// What the IfcIngest runner reports back after parsing + applying the IFC.
public record IngestRunResult(
    bool Success,
    int ThingsCreated,
    int ThingsUpdated,
    int RelationshipsCreated,
    string? Error);

// The service's response to an ingest request.
public record IngestResult(
    bool Success,
    int ThingsCreated,
    int ThingsUpdated,
    int RelationshipsCreated,
    string? Error)
{
    public static IngestResult Ok(int created, int updated, int relsCreated) =>
        new(true, created, updated, relsCreated, null);

    public static IngestResult Failed(string error) => new(false, 0, 0, 0, error);
}
