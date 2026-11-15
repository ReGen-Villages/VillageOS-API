using System.Net;
using vos.Service.Shared.ServiceCalls;

namespace vos.Service.Phloem.Execution;

// Each report for a call record is sent only after the one before it has settled, so the broker never takes a run's end
// before its start.
public sealed class CallReporter
{
    public static readonly TimeSpan FirstPause = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan LongestPause = TimeSpan.FromMinutes(5);

    private readonly IMyceliumGateway _gateway;
    private readonly ILogger _logger;
    private readonly Func<TimeSpan, Task> _pause;
    private readonly Lock _sendingLock = new();
    private readonly Dictionary<Guid, Task> _lastSentFor = new();

    public CallReporter(IMyceliumGateway gateway, ILogger logger, Func<TimeSpan, Task>? pause = null)
    {
        _gateway = gateway;
        _logger = logger;
        _pause = pause ?? (length => Task.Delay(length));
    }

    public static CallReport ReportFor(string status, string? error) => status switch
    {
        _ when !RunStatus.IsFinished(status) => CallReport.Received,
        RunStatus.Succeeded or RunStatus.Rehearsed => CallReport.Completed,
        RunStatus.Cancelled => CallReport.Failed(RunStatus.StoppedBecause(error)),
        _ => CallReport.Failed(string.IsNullOrWhiteSpace(error) ? $"The run ended {status}, and its record gives no reason." : error),
    };

    public static TimeSpan PauseAfter(int failuresInARow) =>
        TimeSpan.FromTicks(Math.Min(LongestPause.Ticks, FirstPause.Ticks * (1L << Math.Clamp(failuresInARow - 1, 0, 30))));

    public Task SendAsync(Guid runId, Guid callRecordId, CallReport report)
    {
        Task sending;
        lock (_sendingLock)
        {
            var before = _lastSentFor.GetValueOrDefault(callRecordId) ?? Task.CompletedTask;
            sending = before.ContinueWith(_ => SendUntilSettledAsync(runId, callRecordId, report), TaskScheduler.Default).Unwrap();
            _lastSentFor[callRecordId] = sending;
        }
        _ = sending.ContinueWith(_ =>
        {
            lock (_sendingLock)
                if (_lastSentFor.TryGetValue(callRecordId, out var last) && last == sending)
                    _lastSentFor.Remove(callRecordId);
        }, TaskScheduler.Default);
        return sending;
    }

    public async Task<int> SendOwedAsync(CancellationToken cancellationToken)
    {
        var owed = await _gateway.OwedReportsAsync(cancellationToken);
        foreach (var run in owed)
            _ = SendAsync(run.RunId, run.CallRecordId, ReportFor(run.Status, run.Error));
        return owed.Count;
    }

    private async Task SendUntilSettledAsync(Guid runId, Guid callRecordId, CallReport report)
    {
        for (var failuresInARow = 0; ; failuresInARow++)
        {
            try
            {
                await _gateway.ReportCallAsync(callRecordId, report);
                if (failuresInARow > 0)
                    _logger.LogInformation("The broker took the {Report} report of run {RunId} after {Failures} sends that did not reach it",
                        report.Word, runId, failuresInARow);
                break;
            }
            catch (CallRecordNoLongerWaitingException noLongerWaiting)
            {
                _logger.LogInformation("Run {RunId} stops reporting: {Answer}", runId, noLongerWaiting.Message);
                break;
            }
            catch (HttpRequestException gone) when (gone.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("Run {RunId} stops reporting, because the broker no longer holds call record {CallRecordId}", runId, callRecordId);
                break;
            }
            catch (HttpRequestException refused) when (refused.StatusCode is { } status && (int)status < 500)
            {
                _logger.LogWarning(refused, "The broker refused the {Report} report of run {RunId} for call record {CallRecordId}, and would refuse it again",
                    report.Word, runId, callRecordId);
                break;
            }
            catch (Exception unsent)
            {
                if (failuresInARow == 0)
                    _logger.LogWarning(unsent, "The {Report} report of run {RunId} did not reach the broker; it is sent again, each pause longer, until it does",
                        report.Word, runId);
                await _pause(PauseAfter(failuresInARow + 1));
            }
        }

        if (!report.FinishesTheCall) return;
        try
        {
            await _gateway.ReleaseRunAsync(runId, CancellationToken.None);
        }
        catch (Exception unreleased)
        {
            _logger.LogWarning(unreleased, "Run {RunId} sent its last report and is still held, so a restart sends that report again", runId);
        }
    }
}
