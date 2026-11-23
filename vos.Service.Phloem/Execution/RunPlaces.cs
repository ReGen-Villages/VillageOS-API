namespace vos.Service.Phloem.Execution;

public sealed record RunJoining(long? QueueNumber, IReadOnlyList<Action> HandedOver);

public sealed class RunPlaces(TimeProvider? real = null)
{
    private readonly TimeProvider _real = real ?? TimeProvider.System;

    public int PipelinesHeld => 0;

    public RunJoining Join(Guid pipelineId, Guid runId, int? limit, Action begin) => new(null, []);

    public void JoinAgain(Guid pipelineId, Guid runId, long queueNumber, Action begin) => _real.GetUtcNow();

    public void Take(Guid pipelineId, Guid runId, int? limit) => _real.GetUtcNow();

    public bool Leave(Guid pipelineId, Guid runId) => false;

    public IReadOnlyList<Action> HandOver(Guid pipelineId, int? limit) => [];
}
