namespace vos.Service.Phloem.Execution;

// How long finished runs are kept, in model days, for runs that ended well and for runs that failed. None means kept.
public sealed record KeepPeriods(double? PassedDays, double? FailedDays);

// A run as the clearing reads it. Held means the orchestrator still holds it, which it does until a run started by a
// state entry has reported its end.
public sealed record RunToJudge(Guid RunId, Guid? PipelineId, string Status, DateTimeOffset? FinishedAt, bool Held);

public sealed record RunsToJudge(IReadOnlyList<RunToJudge> Runs, KeepPeriods Kind, IReadOnlyDictionary<Guid, KeepPeriods> Pipelines);

public static class RunRetention
{
    // A pipeline's own period wins over the run kind's, each for passed and failed runs apart.
    public static bool IsDue(RunToJudge run, KeepPeriods kind, KeepPeriods? pipeline, DateTimeOffset now)
    {
        if (run.Held || !RunStatus.IsFinished(run.Status) || run.FinishedAt is not { } finishedAt) return false;
        var failed = run.Status is RunStatus.Failed or RunStatus.Refused;
        var days = failed ? pipeline?.FailedDays ?? kind.FailedDays : pipeline?.PassedDays ?? kind.PassedDays;
        return days is { } kept && now - finishedAt > TimeSpan.FromDays(kept);
    }
}
