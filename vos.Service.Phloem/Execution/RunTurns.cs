namespace vos.Service.Phloem.Execution;

// One turn at a time per run, so a resume waits for the sweep or the resume already working on the same run. A run's
// entry is dropped once nobody holds or waits for its turn.
public sealed class RunTurns
{
    private readonly Dictionary<Guid, (SemaphoreSlim Gate, int Takers)> _turns = new();

    public async Task<IDisposable> TakeAsync(Guid runId, CancellationToken cancellationToken)
    {
        SemaphoreSlim gate;
        lock (_turns)
        {
            var (held, takers) = _turns.TryGetValue(runId, out var known) ? known : (new SemaphoreSlim(1, 1), 0);
            _turns[runId] = (held, takers + 1);
            gate = held;
        }
        try
        {
            await gate.WaitAsync(cancellationToken);
        }
        catch
        {
            Leave(runId);
            throw;
        }
        return new Turn(this, runId, gate);
    }

    private void Leave(Guid runId)
    {
        lock (_turns)
        {
            var (gate, takers) = _turns[runId];
            if (takers == 1) _turns.Remove(runId);
            else _turns[runId] = (gate, takers - 1);
        }
    }

    private sealed class Turn(RunTurns turns, Guid runId, SemaphoreSlim gate) : IDisposable
    {
        public void Dispose()
        {
            gate.Release();
            turns.Leave(runId);
        }
    }
}
