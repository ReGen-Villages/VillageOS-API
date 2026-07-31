using System.Collections.Concurrent;

namespace vos.Service.Xylem.Services;

public enum IngestJobStatus { Running, Succeeded, Failed }

// A background ingest and its outcome. Result is null while Running.
public record IngestJob(string Id, IngestJobStatus Status, IngestResult? Result);

// In-memory registry of async ingest jobs (#5845) so a large ingest returns a job id
// immediately and its progress is polled at GET /ingest/jobs/{id}. Thread-safe.
public sealed class IngestJobStore
{
    private readonly ConcurrentDictionary<string, IngestJob> _jobs = new();

    public IngestJob Create()
    {
        var job = new IngestJob(Guid.NewGuid().ToString("N"), IngestJobStatus.Running, null);
        _jobs[job.Id] = job;
        return job;
    }

    public void Complete(string id, IngestResult result) =>
        _jobs[id] = new IngestJob(id, result.Success ? IngestJobStatus.Succeeded : IngestJobStatus.Failed, result);

    public IngestJob? Get(string id) => _jobs.TryGetValue(id, out var job) ? job : null;
}
