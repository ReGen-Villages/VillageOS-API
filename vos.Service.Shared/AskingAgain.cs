namespace vos.Service.Shared;

public static class AskingAgain
{
    private static readonly TimeSpan FirstPause = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan LongestPause = TimeSpan.FromMinutes(5);

    // Capped, so a broker that names an hour is asked again within minutes of being ready.
    public static TimeSpan PauseAfter(int failuresInARow, TimeSpan? retryAfter = null) =>
        retryAfter is { } named
            ? (named < LongestPause ? named : LongestPause)
            : TimeSpan.FromTicks(Math.Min(LongestPause.Ticks, FirstPause.Ticks * (1L << Math.Clamp(failuresInARow - 1, 0, 30))));
}
