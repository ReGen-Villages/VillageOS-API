namespace vos.Service.Intake.Services;

/// <summary>Takes the files of submissions the model has let go, once an hour: the retention pass runs on
/// its own clock in the command line, so the store checks rather than being told.</summary>
public sealed class DocumentReclaimService(SharedDocumentService documents, ILogger<DocumentReclaimService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await documents.ReclaimAsync(stoppingToken); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogWarning(error, "The shared files could not be reclaimed this hour: {Reason}", error.Message);
            }
            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
