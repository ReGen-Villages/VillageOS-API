using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using vos.Service.Phloem.Model;
using vos.Service.Shared;
using vos.Service.Shared.Hosting;

namespace vos.Service.Phloem.Execution;

public sealed class CadenceKeeper : BackgroundService
{
    private static readonly TimeSpan BetweenLooks = TimeSpan.FromSeconds(1);

    private readonly IMyceliumGateway _gateway;
    private readonly PipelineStarter _starter;
    private readonly PipelineExecutor _executor;
    private readonly ModelClock _clock;
    private readonly TimeProvider _real;
    private readonly ILogger<CadenceKeeper> _logger;
    private readonly Dictionary<Guid, string> _reported = new();
    private (DateTimeOffset Model, DateTimeOffset Real)? _lastLook;

    public CadenceKeeper(IMyceliumGateway gateway, PipelineStarter starter, PipelineExecutor executor, ModelClock clock,
        ILogger<CadenceKeeper> logger, TimeProvider? real = null)
    {
        _gateway = gateway;
        _starter = starter;
        _executor = executor;
        _clock = clock;
        _logger = logger;
        _real = real ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failuresInARow = 0;
        try
        {
            while (true)
            {
                await Task.Delay(WaitBeforeLook(failuresInARow), _real, stoppingToken);
                failuresInARow = await LookReportingFailuresAsync(failuresInARow, stoppingToken);
            }
        }
        catch (OperationCanceledException) { }
    }

    // Answers how many looks in a row have failed, saying so only when the first fails and when one works again.
    public async Task<int> LookReportingFailuresAsync(int failuresInARow, CancellationToken cancellationToken)
    {
        try
        {
            await LookAsync(cancellationToken);
            if (failuresInARow > 0) _logger.LogInformation("Looking at the cadences works again");
            return 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (failuresInARow == 0) _logger.LogWarning(ex, "Looking at the cadences failed; looking again less often until it works");
            return failuresInARow + 1;
        }
    }

    // A broker that cannot be reached, or that refuses this orchestrator's key, is asked less often the longer it
    // keeps failing, so the key exchange it rate-limits for every service on the machine is not spent here.
    public static TimeSpan WaitBeforeLook(int failuresInARow) =>
        failuresInARow == 0 ? BetweenLooks : TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, Math.Min(failuresInARow, 6))));

    // This clock learns the broker's once every ModelClockInterval, so a change of speed reaches it that late and
    // moves it ahead in one step. The window allows twice the model time the clock's speed covers in that interval
    // and the time since the last look; anything earlier was jumped over. The first look covers nothing behind it.
    public async Task<IReadOnlyList<Task<PipelineRunResult>>> LookAsync(CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var realNow = _real.GetUtcNow();
        var coveredFrom = _lastLook is { } last
            ? Later(last.Model, now - 2 * (realNow - last.Real + ServiceHost.ModelClockInterval) * _clock.Rate)
            : now;
        _lastLook = (now, realNow);

        var runs = new List<Task<PipelineRunResult>>();
        foreach (var cadence in await _gateway.CadencesAsync(cancellationToken))
        {
            var due = CadenceSchedule.DueAt(cadence, now, coveredFrom);
            if (due.Count == 0) continue;

            // Written before any run starts, so a failure in between loses a run rather than starting it twice.
            try
            {
                await _gateway.SetLastDueAsync(cadence.Id, due[^1], cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ReportUnlessAlreadySaid(cadence, ex,"Cadence '{Cadence}' could not be marked due, so nothing is started from it");
                continue;
            }

            var start = await _starter.ResolveAsync(cadence.Id, null, cancellationToken);
            if (!start.Started)
            {
                ReportUnlessAlreadySaid(cadence, null,"Cadence '{Cadence}' starts nothing: " + start.Refusal);
                continue;
            }
            if (_reported.Remove(cadence.Id)) _logger.LogInformation("Cadence '{Cadence}' starts runs again", cadence.Name);
            var subject = new RunSubject(cadence.Id, cadence.Name);
            foreach (var instant in due)
            {
                var parameters = JsonSerializer.SerializeToElement(new Dictionary<string, string> { [ModelNames.Due] = instant.ToString("o") });
                runs.Add(Task.Run(() => _executor.RunAsync(start.PipelineId!.Value, parameters, CancellationToken.None, subject: subject)));
            }
        }
        return runs;
    }

    private static DateTimeOffset Later(DateTimeOffset first, DateTimeOffset second) => first > second ? first : second;

    // Looked at every second, so a cadence is reported when what is wrong with it changes, not on every look.
    private void ReportUnlessAlreadySaid(Cadence cadence, Exception? failure, string message)
    {
        if (_reported.TryGetValue(cadence.Id, out var said) && said == message) return;
        _reported[cadence.Id] = message;
        _logger.LogWarning(failure, message, cadence.Name);
    }
}
