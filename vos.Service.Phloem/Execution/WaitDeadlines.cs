namespace vos.Service.Phloem.Execution;

// The model instants the waits held now fail at, kept in deadline order so finding what is due costs the earliest
// entries and not the whole list. An entry leaves when its wait ends, however it ends.
public sealed class WaitDeadlines
{
    private readonly object _lock = new();
    private readonly SortedSet<(DateTimeOffset Until, Guid RunId, Guid NodeId)> _byDeadline = new();
    private readonly Dictionary<(Guid RunId, Guid NodeId), DateTimeOffset> _byNode = new();

    public int Count
    {
        get { lock (_lock) return _byNode.Count; }
    }

    public void Hold(Guid runId, Guid nodeId, DateTimeOffset until)
    {
        lock (_lock)
        {
            if (_byNode.Remove((runId, nodeId), out var before)) _byDeadline.Remove((before, runId, nodeId));
            _byNode[(runId, nodeId)] = until;
            _byDeadline.Add((until, runId, nodeId));
        }
    }

    public void Release(Guid runId, Guid nodeId)
    {
        lock (_lock)
            if (_byNode.Remove((runId, nodeId), out var until)) _byDeadline.Remove((until, runId, nodeId));
    }

    public IReadOnlyList<(Guid RunId, Guid NodeId)> TakeDue(DateTimeOffset now)
    {
        lock (_lock)
        {
            var due = new List<(Guid RunId, Guid NodeId)>();
            while (_byDeadline.Count > 0 && _byDeadline.Min.Until <= now)
            {
                var (until, runId, nodeId) = _byDeadline.Min;
                _byDeadline.Remove((until, runId, nodeId));
                _byNode.Remove((runId, nodeId));
                due.Add((runId, nodeId));
            }
            return due;
        }
    }
}
