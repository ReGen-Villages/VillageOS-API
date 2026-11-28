using vos.Service.Phloem.Services;

namespace vos.Service.Phloem.Execution;

public sealed class StartUpSequence(
    MyceliumGateway gateway, PipelineExecutor executor, BeginningGate beginnings, ILogger<StartUpSequence> logger, int port)
{
    // Recovery waits for the registration so it reads a broker that is ready; a refusal for good is logged by the gateway.
    public async Task RunAsync(CancellationToken stopping)
    {
        try
        {
            if (await gateway.RegisterUntilSettledAsync(port, stopping))
                logger.LogInformation("Phloem orchestrator registered with mycelium");
            else
                logger.LogWarning("Phloem orchestrator is not registered with mycelium; start-up recovery runs regardless");

            var listing = await executor.ListHeldRunsAsync(CancellationToken.None);
            beginnings.LetRunsBegin();
            // Sent first, so a run that the recovery below ends reports its start before its end.
            var owed = await executor.SendOwedReportsAsync(listing, CancellationToken.None);
            if (owed > 0) logger.LogInformation("Sending again the reports {Count} run(s) started by a state entry still owe", owed);
            var waits = await executor.RecoverWaitsAsync(listing, CancellationToken.None);
            if (waits.Resumed > 0) logger.LogInformation("Resumed {Count} run(s) whose wait was answered while the orchestrator was away", waits.Resumed);
            if (waits.WatchedAgain > 0) logger.LogInformation("Watched again for {Count} waiting node run(s) whose watch was missing a relationship", waits.WatchedAgain);
            var carriedOn = await executor.RecoverUnfinishedAsync(listing, CancellationToken.None);
            if (carriedOn > 0) logger.LogInformation("Carried {Count} run(s) on from a node that was trying or in the middle of a call", carriedOn);
            var queued = await executor.RecoverQueuedAsync(listing, CancellationToken.None);
            if (queued > 0) logger.LogInformation("Queued {Count} run(s) again that had not begun", queued);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Error during Phloem startup registration");
        }
    }
}
