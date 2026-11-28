namespace vos.Tests.Shared;

// Its timestamps move only when a test or a timer moves them, so a measured time is exactly what the test set.
public sealed class SteppingClock : TimeProvider
{
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Read(ref _ticks);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Advance(dueTime);
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
