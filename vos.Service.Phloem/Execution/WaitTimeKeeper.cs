using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace vos.Service.Phloem.Execution;

// Looks once a real second because the model clock's speed can change under a timer set in model time.
public sealed class WaitTimeKeeper(PipelineExecutor executor, ILogger<WaitTimeKeeper> logger, TimeProvider? real = null) : BackgroundService
{
    private static readonly TimeSpan BetweenLooks = TimeSpan.FromSeconds(1);
    private readonly TimeProvider _real = real ?? TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(BetweenLooks, _real, stoppingToken);
                try { await executor.ExpireDueWaitsAsync(stoppingToken); }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Ending the waits whose time had passed failed");
                }
            }
        }
        catch (OperationCanceledException) { }
    }
}
