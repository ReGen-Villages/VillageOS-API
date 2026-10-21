using System.Collections.Concurrent;

namespace vos.Service.Xylem.Services;

public enum IngestJobStatus { Running, Succeeded, Failed }

// A background ingest and its outcome. Result is null while Running.
public record IngestJob(string Id, IngestJobStatus Status, IngestResult? Result);

// In-memory registry of async ingest jobs so a large ingest returns a job id
// immediately and its progress is polled at GET /ingest/jobs/{id}. Thread-safe.
//
// The host stops it with the service: each running job is cancelled and waited for, so no ingest tool
// outlives the service. The web server stops after it and still takes requests meanwhile, so a job
// started once the stop has begun is failed without running.
public sealed class IngestJobStore : IHostedService
{
    public static readonly IngestResult StoppedWithTheService = IngestResult.Failed(
        "The ingest was stopped before it finished, because the service stopped. "
        + "The model keeps what had already been posted.");

    private readonly ConcurrentDictionary<string, IngestJob> _jobs = new();
    private readonly CancellationTokenSource _serviceStopping = new();
    private readonly Dictionary<string, Task> _running = new();
    private readonly object _gate = new();
    private bool _stopping;

    public string Start(Func<CancellationToken, Task<IngestResult>> ingest)
    {
        var id = Guid.NewGuid().ToString("N");
        _jobs[id] = new IngestJob(id, IngestJobStatus.Running, null);
        lock (_gate)
        {
            if (_stopping) Complete(id, StoppedWithTheService);
            else _running[id] = Task.Run(() => RunAsync(id, ingest));
        }
        return id;
    }

    public IngestJob? Get(string id) => _jobs.TryGetValue(id, out var job) ? job : null;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task[] running;
        lock (_gate)
        {
            _stopping = true;
            running = [.. _running.Values];
        }
        await _serviceStopping.CancelAsync();
        await Task.WhenAll(running).WaitAsync(cancellationToken);
    }

    private async Task RunAsync(string id, Func<CancellationToken, Task<IngestResult>> ingest)
    {
        IngestResult result;
        try { result = await ingest(_serviceStopping.Token); }
        catch (OperationCanceledException) when (_serviceStopping.IsCancellationRequested) { result = StoppedWithTheService; }
        catch (Exception ex) { result = IngestResult.Failed(ex.Message); }

        Complete(id, result);
        lock (_gate) _running.Remove(id);
    }

    private void Complete(string id, IngestResult result) =>
        _jobs[id] = new IngestJob(id, result.Success ? IngestJobStatus.Succeeded : IngestJobStatus.Failed, result);
}
