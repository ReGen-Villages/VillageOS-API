namespace vos.Service.Phloem.Execution;

// How long finished runs are kept, in model days, for runs that ended well and for runs that failed. None means kept.
public sealed record KeepPeriods(double? PassedDays, double? FailedDays);

// A run as the clearing reads it. Held means the orchestrator still holds it, which it does until a run started by a
// state entry has reported its end.
public sealed record RunToJudge(Guid RunId, Guid? PipelineId, string Status, DateTimeOffset? FinishedAt, bool Held);

public sealed record RunsToJudge(IReadOnlyList<RunToJudge> Runs, KeepPeriods Kind, IReadOnlyDictionary<Guid, KeepPeriods> Pipelines, int RunsPerLook);

public static class RunRetention
{
    // Clearing a run is one commit for each Thing it held, each taking the commit lock, so one look must not clear a whole backlog.
    private const int RunsPerLookWhereNoneIsStated = 500;

    public static int RunsPerLookOf(double? stated) =>
        stated is { } number && number >= 1 && number == Math.Floor(number) && number <= int.MaxValue ? (int)number : RunsPerLookWhereNoneIsStated;

    // A pipeline's own period wins over the run kind's, each for passed and failed runs apart.
    public static bool IsDue(RunToJudge run, KeepPeriods kind, KeepPeriods? pipeline, DateTimeOffset now)
    {
        if (run.Held || !RunStatus.IsFinished(run.Status) || run.FinishedAt is not { } finishedAt) return false;
        var failed = run.Status is RunStatus.Failed or RunStatus.Refused;
        var days = failed ? pipeline?.FailedDays ?? kind.FailedDays : pipeline?.PassedDays ?? kind.PassedDays;
        return days is { } kept && now - finishedAt > TimeSpan.FromDays(kept);
    }

    // A stop request made by anyone but the run's maker, such as a person, is theirs, so it stays when its runs are cleared.
    public static bool GoesWithItsRuns(Guid? requestMaker, Guid? runMaker) => requestMaker == runMaker;

    // A stop request names the run whose stop box wrote it and each run it stopped, and goes once none but this one is left.
    public static IReadOnlyList<Guid> StopRequestsGoingWith(Guid runId, IReadOnlyDictionary<Guid, IReadOnlySet<Guid>> runsEachRequestNames) =>
        runsEachRequestNames.Where(request => request.Value.All(named => named == runId)).Select(request => request.Key).ToList();
}
