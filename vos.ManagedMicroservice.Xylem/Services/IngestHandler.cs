namespace vos.ManagedMicroservice.Xylem.Services;

/// <summary>Orchestrates one ingest: validate the request, run IfcIngest via the runner, surface the
/// counts. Kept free of transport/subprocess details so it is fully unit-tested (#5842).</summary>
public class IngestHandler
{
    private readonly IIfcIngestRunner _runner;
    private readonly IModelPreparer _preparer;

    public IngestHandler(IIfcIngestRunner runner, IModelPreparer preparer)
    {
        _runner = runner;
        _preparer = preparer;
    }

    /// <summary>Ingest an uploaded stream: check size, spool to a temp file, run, always clean up. Keeps
    /// the endpoint a thin form-read so the whole upload path is unit-tested without a web host.</summary>
    public async Task<IngestResult> IngestUploadAsync(
        Stream ifc, long length, string modelName, IngestMode mode, long maxBytes, CancellationToken ct)
    {
        if (length <= 0) return IngestResult.Failed("No IFC content uploaded.");
        if (length > maxBytes) return IngestResult.Failed($"File exceeds the {maxBytes / (1024 * 1024)} MB upload limit.");

        var temp = Path.Combine(Path.GetTempPath(), $"xylem_{Guid.NewGuid():N}.ifc");
        try
        {
            await using (var fs = File.Create(temp))
                await ifc.CopyToAsync(fs, ct);
            return await IngestAsync(modelName, mode, temp, ct);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public async Task<IngestResult> IngestAsync(string modelName, IngestMode mode, string ifcPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(modelName))
            return IngestResult.Failed("A model name is required.");
        if (!File.Exists(ifcPath))
            return IngestResult.Failed("Uploaded IFC file not found.");

        // A new-model ingest replaces the current model: clear it first, then ingest into the empty model.
        if (mode == IngestMode.NewModel)
        {
            var clearError = await _preparer.ClearModelAsync(ct);
            if (clearError is not null) return IngestResult.Failed(clearError);
        }

        var r = await _runner.RunAsync(ifcPath, modelName, ct);
        return r.Success
            ? IngestResult.Ok(r.ThingsCreated, r.ThingsUpdated, r.RelationshipsCreated)
            : IngestResult.Failed(r.Error ?? "IFC ingest failed.");
    }
}
