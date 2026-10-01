namespace vos.Tests.Shared;

// Waits for a condition a test cannot await directly — a background loop's effect, a write nothing
// awaits. The ceiling is a hang detector, not a deadline the work is meant to approach: a test that
// instead waits a fixed duration is asserting how busy the build agent is, so it passes on an idle
// machine and reddens an unrelated pull request on a loaded one.
public static class Settle
{
    // The one clock a test may put on a wait for something to happen. TestWaitsKeepTheSharedCeilingTests
    // refuses any other, because shorter ones have failed correct code on the shared build agent.
    public static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(30);

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

    public static async Task ForAsync(Task work, string expectation)
    {
        try
        {
            await work.WaitAsync(Ceiling);
        }
        catch (TimeoutException) when (!work.IsCompleted)
        {
            throw new TimeoutException(
                $"Waited {Ceiling.TotalSeconds:0.#} seconds and it never happened: {expectation}");
        }
    }

    // The only short clock a test may keep: time for something to happen before the test asserts
    // that it did not. A machine too busy to get to it lets such a test pass without proof, and can
    // never make it fail.
    public static Task BeforeAssertingAbsenceAsync(TimeSpan window) => Task.Delay(window);
}
