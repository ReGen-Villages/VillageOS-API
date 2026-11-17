using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using vos.Service.Shared;

namespace vos.Service.Phloem.Execution;

// Looks once a real hour and judges a run's age on the model clock, as decisions are retired, so the history a
// fast simulation keeps is the model's.
public sealed class RunClearingKeeper(IMyceliumGateway gateway, ModelClock clock, ILogger<RunClearingKeeper> logger, TimeProvider? real = null)
    : BackgroundService
{
    private static readonly TimeSpan BetweenLooks = TimeSpan.FromHours(1);
    private readonly TimeProvider _real = real ?? TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(BetweenLooks, _real, stoppingToken);
                try { await ClearOnceAsync(stoppingToken); }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Clearing the runs kept past their period failed");
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    // Answers how many runs were cleared and how many Things went with them. A run that cannot be cleared is left
    // for the next look.
    public async Task<(int Runs, int Things)> ClearOnceAsync(CancellationToken cancellationToken)
    {
        var judged = await gateway.RunsToJudgeAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var (runs, things) = (0, 0);
        foreach (var run in judged.Runs)
        {
            var pipeline = run.PipelineId is { } pipelineId ? judged.Pipelines.GetValueOrDefault(pipelineId) : null;
            if (!RunRetention.IsDue(run, judged.Kind, pipeline, now)) continue;
            try
            {
                things += await gateway.ClearRunAsync(run.RunId, cancellationToken);
                runs++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Run {RunId} could not be cleared; the next look tries again", run.RunId);
            }
        }
        if (runs > 0) logger.LogInformation("Cleared {Runs} run(s) kept past their period, {Things} Thing(s) in all", runs, things);
        return (runs, things);
    }
}
