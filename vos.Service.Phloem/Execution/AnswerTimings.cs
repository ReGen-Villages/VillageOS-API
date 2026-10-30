using System.Diagnostics;

namespace vos.Service.Phloem.Execution;

// The broker gives up on a request Phloem has not answered in time, so each step before the answer is timed.
public sealed class AnswerTimings
{
    private readonly Stopwatch _sinceArrival = Stopwatch.StartNew();
    private readonly List<(string Step, long Milliseconds)> _steps = [];

    public long TotalMilliseconds => _sinceArrival.ElapsedMilliseconds;

    public string Steps
    {
        get { lock (_steps) return string.Join(", ", _steps.Select(step => $"{step.Step} {step.Milliseconds} ms")); }
    }

    public async Task<T> TimeAsync<T>(string step, Func<Task<T>> work)
    {
        var running = Stopwatch.StartNew();
        try { return await work(); }
        finally { lock (_steps) _steps.Add((step, running.ElapsedMilliseconds)); }
    }
}
