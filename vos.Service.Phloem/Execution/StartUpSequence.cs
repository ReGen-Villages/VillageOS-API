using System.Net;
using vos.Service.Phloem.Services;
using vos.Service.Shared;

namespace vos.Service.Phloem.Execution;

public sealed class StartUpSequence(
    MyceliumGateway gateway, PipelineExecutor executor, BeginningGate beginnings, ILogger<StartUpSequence> logger, int port)
{
    // The listing comes before the registration, so no run this orchestrator begins is in it; the recovery still waits
    // for the registration so it writes to a broker that is ready, and a refusal for good is logged by the gateway.
    public async Task RunAsync(CancellationToken stopping)
    {
        using var stoppingShutsTheGate = stopping.Register(beginnings.Stop);
        try
        {
            HeldRunsListing? listing;
            try { listing = await ListUntilAnsweredAsync(stopping); }
            finally { beginnings.LetRunsBegin(); }

            if (await gateway.RegisterUntilSettledAsync(port, stopping))
                logger.LogInformation("Phloem orchestrator registered with mycelium");
            else
                logger.LogWarning("Phloem orchestrator is not registered with mycelium; start-up recovery runs regardless");
            if (listing is null) return;

            // Sent first, so a run that the recovery below ends reports its start before its end.
            var owed = executor.SendOwedReports(listing);
            if (owed > 0) logger.LogInformation("Sending again the reports {Count} run(s) started by a state entry still owe", owed);
            var waits = await executor.RecoverWaitsAsync(listing, CancellationToken.None);
            if (waits.Resumed > 0) logger.LogInformation("Resumed {Count} run(s) whose wait was answered while the orchestrator was away", waits.Resumed);
            if (waits.WatchedAgain > 0) logger.LogInformation("Watched again for {Count} waiting node run(s) whose watch was missing a relationship", waits.WatchedAgain);
            var carriedOn = await executor.RecoverUnfinishedAsync(listing, CancellationToken.None);
            if (carriedOn > 0) logger.LogInformation("Carried {Count} run(s) on from a node that was trying or in the middle of a call", carriedOn);
            var queued = await executor.RecoverQueuedAsync(listing);
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

    // A listing refused or failed leaves nothing to recover, and runs begin all the same.
    private async Task<HeldRunsListing?> ListUntilAnsweredAsync(CancellationToken stopping)
    {
        for (var asks = 1; ; asks++)
        {
            try
            {
                return await executor.ListHeldRunsAsync(stopping);
            }
            catch (Exception notYet) when (!stopping.IsCancellationRequested && MayAnswerLater(notYet))
            {
                if (asks == 1)
                    logger.LogWarning("Mycelium could not list the runs this orchestrator holds yet ({Why}); it is asked again, each pause longer, until it can",
                        notYet.Message);
                await Task.Delay(AskingAgain.PauseAfter(asks), stopping);
            }
            catch (Exception refused) when (!stopping.IsCancellationRequested)
            {
                logger.LogError(refused, "The runs this orchestrator holds could not be listed, so nothing is recovered and runs begin");
                return null;
            }
        }
    }

    private static bool MayAnswerLater(Exception error) => error switch
    {
        HttpRequestException { StatusCode: null or HttpStatusCode.ServiceUnavailable or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests } => true,
        TaskCanceledException => true,
        _ => false,
    };
}
