using System.Collections.Concurrent;
using vos.Service.Shared.DagNode;

namespace vos.Service.Tributary.Services;

public sealed class PassedOnRequestLimitWarnings
{
    public const int MostEndpointsRemembered = 1000;

    private readonly TimeSpan? _limit;
    private readonly ILogger<PassedOnRequestLimitWarnings> _logger;
    private readonly ConcurrentDictionary<Guid, byte> _endpointsWarnedAbout = new();

    public PassedOnRequestLimitWarnings(PassedOnRequestLimit limit, ILogger<PassedOnRequestLimitWarnings> logger)
    {
        _limit = limit.Limit;
        _logger = logger;
    }

    public void WarnWhenTooShortToAnswerFirst()
    {
        if (_limit is not { } limit || PassedOnRequestDeadline.LeavesTheMargin(limit))
            return;

        _logger.LogWarning(
            "The platform allows {PassedOnRequestLimitSeconds} seconds for a passed-on request, too short to leave " +
            "Tributary {MarginSeconds} seconds to answer with its own reason. Raise Mycelium's " +
            "DaemonLauncher:PassedOnRequestLimitSeconds above {MarginSeconds}.",
            limit.TotalSeconds, PassedOnRequestDeadline.MarginForTheAnswer.TotalSeconds,
            PassedOnRequestDeadline.MarginForTheAnswer.TotalSeconds);
    }

    public void WarnOnceWhenNotShorter(Guid endpointId, string endpointName, TimeSpan statedTimeout)
    {
        if (_limit is not { } limit || statedTimeout < limit || !_endpointsWarnedAbout.TryAdd(endpointId, 0))
            return;

        // Counting takes every lock in the dictionary, so it is done only after a new endpoint is recorded.
        if (_endpointsWarnedAbout.Count > MostEndpointsRemembered)
        {
            _endpointsWarnedAbout.Clear();
            _endpointsWarnedAbout.TryAdd(endpointId, 0);
        }

        _logger.LogWarning(
            "Endpoint {EndpointName} has a timeout of {TimeoutSeconds} seconds, which is not shorter than the " +
            "{PassedOnRequestLimitSeconds} seconds Mycelium allows a request it passes on, so Mycelium can give up " +
            "before the endpoint does. Set the endpoint's timeout property below {PassedOnRequestLimitSeconds} seconds.",
            endpointName, statedTimeout.TotalSeconds, limit.TotalSeconds, limit.TotalSeconds);
    }
}

public enum AwaitedStep
{
    EndpointSettings,
    DataSource,
    Reshaping,
    ReadingsWrite,
    ResponseKept,
}

public sealed class TributaryRequestDeadline(PassedOnRequestDeadline deadline)
{
    public CancellationToken Token => deadline.Token;
    public bool HasPassed => deadline.HasPassed;
    public TimeSpan? TimeLeft => deadline.TimeLeft;
    public AwaitedStep Awaiting { get; set; } = AwaitedStep.EndpointSettings;

    public string Reason(string? endpointName)
    {
        var what = Awaiting switch
        {
            AwaitedStep.EndpointSettings => $"The broker had not answered with the settings of endpoint {endpointName}",
            AwaitedStep.DataSource => "The data source had not answered",
            AwaitedStep.Reshaping => "Tributary had not finished reshaping the answer",
            AwaitedStep.ReadingsWrite => "The broker had not accepted the readings",
            _ => "The broker had not kept the response",
        };
        return $"{what} within {deadline.Allowed.TotalSeconds:0.###} of the {deadline.Limit.TotalSeconds:0.###} seconds " +
               "the platform allows a passed-on request.";
    }
}
