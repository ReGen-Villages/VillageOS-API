namespace vos.Service.Phloem.Execution;

// Shut until the start-up recovery holds its listing, so no run this orchestrator begins can be in it. It settles once.
public sealed class BeginningGate
{
    private readonly TaskCompletionSource<bool> _settled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static BeginningGate Open()
    {
        var gate = new BeginningGate();
        gate.LetRunsBegin();
        return gate;
    }

    public void LetRunsBegin() => _settled.TrySetResult(true);

    public void Stop() => _settled.TrySetResult(false);

    // False once the orchestrator is stopping, so a beginning that waited records nothing.
    public Task<bool> UntilSettledAsync(CancellationToken cancellationToken) => Task.FromResult(true);
}
