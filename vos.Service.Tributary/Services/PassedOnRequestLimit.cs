using System.Collections.Concurrent;

namespace vos.Service.Tributary.Services;

public sealed class PassedOnRequestLimit
{
    public const int MostEndpointsRemembered = 1000;

    private readonly TimeSpan? _limit;
    private readonly TimeProvider _clock;
    private readonly ILogger<PassedOnRequestLimit> _logger;
    private readonly ConcurrentDictionary<Guid, byte> _endpointsWarnedAbout = new();

    public PassedOnRequestLimit(TimeSpan? limit, TimeProvider clock, ILogger<PassedOnRequestLimit> logger)
    {
        _limit = limit;
        _clock = clock;
        _logger = logger;
    }

    public PassedOnRequestDeadline StartDeadline(CancellationToken requestAborted) =>
        _limit is { } limit
            ? PassedOnRequestDeadline.Start(limit, _clock, requestAborted)
            : PassedOnRequestDeadline.None(requestAborted);

    public void WarnOnceWhenNotShorter(Guid endpointId, string endpointName, TimeSpan statedTimeout)
    {
        if (_limit is not { } limit || statedTimeout < limit || _endpointsWarnedAbout.ContainsKey(endpointId))
            return;

        // Counting takes every lock in the dictionary, so it is done only on the way to a warning.
        if (_endpointsWarnedAbout.Count >= MostEndpointsRemembered)
            _endpointsWarnedAbout.Clear();
        if (!_endpointsWarnedAbout.TryAdd(endpointId, 0))
            return;

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
    ReadingsWrite,
    ResponseKept,
}

public sealed class PassedOnRequestDeadline : IDisposable
{
    // Time for the answer to reach Mycelium before Mycelium's own limit cuts the request off.
    public static readonly TimeSpan MarginForTheAnswer = TimeSpan.FromSeconds(2);

    private readonly CancellationTokenSource? _deadline;
    private readonly CancellationTokenSource? _deadlineOrAborted;
    private readonly TimeSpan _limit;
    private readonly TimeSpan _allowed;

    public CancellationToken Token { get; }
    public AwaitedStep Awaiting { get; set; } = AwaitedStep.EndpointSettings;
    public bool HasPassed => _deadline?.IsCancellationRequested == true;

    private PassedOnRequestDeadline(
        CancellationToken token, CancellationTokenSource? deadline, CancellationTokenSource? deadlineOrAborted,
        TimeSpan limit, TimeSpan allowed)
    {
        Token = token;
        _deadline = deadline;
        _deadlineOrAborted = deadlineOrAborted;
        _limit = limit;
        _allowed = allowed;
    }

    public static PassedOnRequestDeadline None(CancellationToken requestAborted) =>
        new(requestAborted, null, null, TimeSpan.Zero, TimeSpan.Zero);

    public static PassedOnRequestDeadline Start(TimeSpan limit, TimeProvider clock, CancellationToken requestAborted)
    {
        var allowed = limit > MarginForTheAnswer ? limit - MarginForTheAnswer : TimeSpan.Zero;
        var deadline = new CancellationTokenSource(allowed, clock);
        var deadlineOrAborted = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, requestAborted);
        return new PassedOnRequestDeadline(deadlineOrAborted.Token, deadline, deadlineOrAborted, limit, allowed);
    }

    public string Reason(string endpointName)
    {
        var what = Awaiting switch
        {
            AwaitedStep.EndpointSettings => $"The broker had not answered with the settings of endpoint {endpointName}",
            AwaitedStep.DataSource => "The data source had not answered",
            AwaitedStep.ReadingsWrite => "The broker had not accepted the readings",
            _ => "The broker had not kept the response",
        };
        return $"{what} within {_allowed.TotalSeconds:0.###} of the {_limit.TotalSeconds:0.###} seconds " +
               "the platform allows a passed-on request.";
    }

    public void Dispose()
    {
        _deadlineOrAborted?.Dispose();
        _deadline?.Dispose();
    }
}
