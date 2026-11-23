namespace vos.Service.Phloem.Execution;

// QueueNumber is null for a run that has its place now. HandedOver begins each run queued before it that has one now.
public sealed record RunJoining(long? QueueNumber, IReadOnlyList<Action> HandedOver);

// Which runs of each pipeline are going, and which are queued for a place, lowest number first. A pipeline with no
// limit and nothing queued is not held at all.
public sealed class RunPlaces(TimeProvider? real = null)
{
    private sealed class Pipeline
    {
        public HashSet<Guid> Going { get; } = new();
        public SortedDictionary<(long Number, Guid RunId), Action> Queued { get; } = new();

        public List<(Guid RunId, Action Begin)> TakeFree(int? limit)
        {
            var taken = new List<(Guid, Action)>();
            while (Queued.Count > 0 && (limit is not { } most || Going.Count < most))
            {
                var ((number, runId), begin) = Queued.First();
                Queued.Remove((number, runId));
                Going.Add(runId);
                taken.Add((runId, begin));
            }
            return taken;
        }
    }

    private readonly TimeProvider _real = real ?? TimeProvider.System;
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, Pipeline> _pipelines = new();
    private long _lastNumber;

    public int PipelinesHeld
    {
        get { lock (_lock) return _pipelines.Count; }
    }

    // A start joins the queue even where a place is free, so it never passes a run already queued.
    public RunJoining Join(Guid pipelineId, Guid runId, int? limit, Action begin)
    {
        lock (_lock)
        {
            if (HeldWithin(pipelineId, limit) is not { } pipeline) return new RunJoining(null, []);
            // The real clock, because the model's can stand still or be set back, and a number is compared with those
            // given before a restart.
            _lastNumber = Math.Max(_lastNumber + 1, _real.GetUtcNow().ToUnixTimeMilliseconds());
            pipeline.Queued.Add((_lastNumber, runId), begin);
            var taken = pipeline.TakeFree(limit);
            return new RunJoining(
                pipeline.Going.Contains(runId) ? null : _lastNumber,
                taken.Where(run => run.RunId != runId).Select(run => run.Begin).ToList());
        }
    }

    public void JoinAgain(Guid pipelineId, Guid runId, long queueNumber, Action begin)
    {
        lock (_lock)
        {
            _lastNumber = Math.Max(_lastNumber, queueNumber);
            var pipeline = Held(pipelineId);
            if (!pipeline.Going.Contains(runId)) pipeline.Queued.TryAdd((queueNumber, runId), begin);
        }
    }

    // For a run that has begun already and goes on: it takes a place even past the limit.
    public void Take(Guid pipelineId, Guid runId, int? limit)
    {
        lock (_lock) HeldWithin(pipelineId, limit)?.Going.Add(runId);
    }

    // Answers whether the place it left is wanted by a queued run.
    public bool Leave(Guid pipelineId, Guid runId)
    {
        lock (_lock)
        {
            if (!_pipelines.TryGetValue(pipelineId, out var pipeline) || !pipeline.Going.Remove(runId)) return false;
            if (pipeline.Queued.Count > 0) return true;
            if (pipeline.Going.Count == 0) _pipelines.Remove(pipelineId);
            return false;
        }
    }

    public IReadOnlyList<Action> HandOver(Guid pipelineId, int? limit)
    {
        lock (_lock)
            return _pipelines.TryGetValue(pipelineId, out var pipeline) ? pipeline.TakeFree(limit).Select(run => run.Begin).ToList() : [];
    }

    private Pipeline? HeldWithin(Guid pipelineId, int? limit) =>
        limit is null ? _pipelines.GetValueOrDefault(pipelineId) : Held(pipelineId);

    private Pipeline Held(Guid pipelineId)
    {
        if (!_pipelines.TryGetValue(pipelineId, out var pipeline))
            _pipelines[pipelineId] = pipeline = new Pipeline();
        return pipeline;
    }
}
