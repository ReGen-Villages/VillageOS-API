namespace vos.Service.Shared.DagNode;

public sealed class PassedOnRequestLimit(TimeSpan? limit, TimeProvider clock)
{
    public TimeSpan? Limit { get; } = limit;

    public PassedOnRequestDeadline StartDeadline(CancellationToken requestAborted) =>
        Limit is { } value
            ? PassedOnRequestDeadline.Start(value, clock, requestAborted)
            : PassedOnRequestDeadline.None(requestAborted);
}

public sealed class PassedOnRequestDeadline : IDisposable
{
    // Time for the answer to reach Mycelium before Mycelium's own limit cuts the request off.
    public static readonly TimeSpan MarginForTheAnswer = TimeSpan.FromSeconds(2);

    public static bool LeavesTheMargin(TimeSpan limit) => limit > MarginForTheAnswer;

    private readonly CancellationTokenSource? _deadline;
    private readonly CancellationTokenSource? _deadlineOrAborted;
    private readonly TimeProvider? _clock;
    private readonly long _startedAt;

    public CancellationToken Token { get; }
    public TimeSpan Limit { get; }
    public TimeSpan Allowed { get; }
    public bool HasPassed => _deadline?.IsCancellationRequested == true;

    // Null when the platform gave no limit.
    public TimeSpan? TimeLeft
    {
        get
        {
            if (_clock is null) return null;
            var left = Allowed - _clock.GetElapsedTime(_startedAt);
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    private PassedOnRequestDeadline(
        CancellationToken token, CancellationTokenSource? deadline, CancellationTokenSource? deadlineOrAborted,
        TimeProvider? clock, TimeSpan limit, TimeSpan allowed)
    {
        Token = token;
        _deadline = deadline;
        _deadlineOrAborted = deadlineOrAborted;
        _clock = clock;
        _startedAt = clock?.GetTimestamp() ?? 0;
        Limit = limit;
        Allowed = allowed;
    }

    public static PassedOnRequestDeadline None(CancellationToken requestAborted) =>
        new(requestAborted, null, null, null, TimeSpan.Zero, TimeSpan.Zero);

    public static PassedOnRequestDeadline Start(TimeSpan limit, TimeProvider clock, CancellationToken requestAborted)
    {
        var allowed = LeavesTheMargin(limit) ? limit - MarginForTheAnswer : limit;
        var deadline = new CancellationTokenSource(allowed, clock);
        var deadlineOrAborted = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, requestAborted);
        return new PassedOnRequestDeadline(deadlineOrAborted.Token, deadline, deadlineOrAborted, clock, limit, allowed);
    }

    public void Dispose()
    {
        _deadlineOrAborted?.Dispose();
        _deadline?.Dispose();
    }
}
