namespace vos.Tests.Shared;

// Waits for a condition a test cannot await directly — a background loop's effect, a fire-and-forget
// write. The ceiling is a hang detector, not a deadline the work is meant to approach: a test that
// instead waits a fixed duration is asserting how busy the build agent is, so it passes on an idle
// machine and reddens an unrelated pull request on a loaded one.
public static class Settle
{
    private static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan BetweenChecks = TimeSpan.FromMilliseconds(10);

    public static async Task UntilAsync(Func<bool> condition, string expectation)
    {
        var deadline = Environment.TickCount64 + (long)Ceiling.TotalMilliseconds;
        while (!condition())
        {
            if (Environment.TickCount64 >= deadline)
                throw new TimeoutException(
                    $"Waited {Ceiling.TotalSeconds:0.#} seconds and it never became true: {expectation}");
            await Task.Delay(BetweenChecks);
        }
    }
}
