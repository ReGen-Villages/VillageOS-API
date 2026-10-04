namespace vos.Service.Forage.Services;

// Starts a run off the request that dispatched it. The platform gives a dispatch fifteen seconds, and a
// run over a catalogue of tens of sources fetched a few at a time outlasts that — so answering first is
// what keeps finished work from being recorded as failed and driven a second time. What ends the dispatch
// instead is the model: the connection names a state, and the record stays in flight until the site
// reaches it.
//
// An interface so a test can run the work where it can be awaited. Nothing else about it varies.
public interface IDiscoveryRunStarter
{
    void Start(Guid subjectId, Func<CancellationToken, Task> run);
}

// The run outlives the request, so it cannot take the request's cancellation token — that one is
// cancelled as the response completes, which would abort every run at the moment it started. It takes
// the host's instead, so a shutdown stops a run in flight and nothing else does.
//
// A run is held back while another run for the same subject is in flight; runs for different subjects
// still run together. A run reads which calls the model holds no answer for, then makes them and records
// what came back, so two runs for one site read the same gaps and both fill them:
//   - each mints a coverage for the same call, and from then on every run for that site fails, because
//     the ledger indexes coverage by the Thing a call is about and its source, and cannot hold two;
//   - each calls the same sources;
//   - each replaces the same vocabulary relationships, and the second replacement is refused;
//   - each writes the analysis relationship, which the model refuses the second time.
// Running one site's discoveries in parallel needs those writes to stop duplicating first, and only then
// can this hold go. It does not cover a source's run overlapping a site's run, which can mint the same
// coverage too, and it holds within one process only.
//
// At most one run waits. A waiting run reads the model only once the run in flight ends, so a dispatch
// arriving while one already waits would find nothing it will not; dropping it keeps a site dispatched
// faster than it is discovered from piling up runs.
public sealed class DiscoveryRunStarter : IDiscoveryRunStarter
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<DiscoveryRunStarter> _logger;
    private readonly Dictionary<Guid, SubjectRuns> _runsBySubject = new();
    private readonly Lock _runsGate = new();

    private sealed class SubjectRuns
    {
        public required Task LastEnded;
        public bool OneWaiting;
    }

    public DiscoveryRunStarter(IHostApplicationLifetime lifetime, ILogger<DiscoveryRunStarter> logger)
    {
        _lifetime = lifetime;
        _logger = logger;
    }

    public int SubjectsWithRunsInFlight
    {
        get { lock (_runsGate) return _runsBySubject.Count; }
    }

    public void Start(Guid subjectId, Func<CancellationToken, Task> run)
    {
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SubjectRuns? runs;
        Task previous;
        var dropped = false;
        lock (_runsGate)
        {
            if (!_runsBySubject.TryGetValue(subjectId, out runs))
            {
                _runsBySubject[subjectId] = new SubjectRuns { LastEnded = ended.Task };
                previous = Task.CompletedTask;
            }
            else if (runs.OneWaiting)
            {
                dropped = true;
                previous = Task.CompletedTask;
            }
            else
            {
                runs.OneWaiting = true;
                previous = runs.LastEnded;
                runs.LastEnded = ended.Task;
            }
        }

        if (dropped)
        {
            _logger.LogInformation(
                "A discovery run for {SubjectId} is already waiting and reads the model after the one in flight; " +
                "this dispatch adds nothing to it", subjectId);
            return;
        }

        var waits = runs != null;
        if (waits)
            _logger.LogInformation(
                "A discovery run for {SubjectId} is in flight; this one starts when it ends", subjectId);

        _ = Task.Run(async () =>
        {
            await previous;
            if (waits)
                lock (_runsGate) runs!.OneWaiting = false;

            // Nothing awaits this task, so an exception escaping here would be unobserved: reported by
            // nothing, and on some configurations taking the process with it.
            try
            {
                await run(_lifetime.ApplicationStopping);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Discovery run failed after its dispatch had been accepted");
            }
            finally
            {
                lock (_runsGate)
                    if (_runsBySubject.TryGetValue(subjectId, out var current) && current.LastEnded == ended.Task)
                        _runsBySubject.Remove(subjectId);
                ended.SetResult();
            }
        });
    }
}
