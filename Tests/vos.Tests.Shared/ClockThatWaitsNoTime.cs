namespace vos.Tests.Shared;

public sealed class ClockThatWaitsNoTime : TimeProvider
{
    public List<TimeSpan> Waits { get; } = [];

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Waits.Add(dueTime);
        _ = Task.Run(() => callback(state));
        return new NothingToStop();
    }

    private sealed class NothingToStop : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
