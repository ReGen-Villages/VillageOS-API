namespace vos.ManagedMicroservice.Xylem.Services;

// Runs the IFC ingest for one uploaded file and reports the counts. The production
// implementation invokes the vos.Tools.IfcIngest tool (Xbim parse + classification + roll-ups) and
// applies the graph to Mycelium; tests substitute a fake so the orchestration is exercised without a
// subprocess or a live broker.
public interface IIfcIngestRunner
{
    Task<IngestRunResult> RunAsync(string ifcPath, string modelName, CancellationToken ct);
}
