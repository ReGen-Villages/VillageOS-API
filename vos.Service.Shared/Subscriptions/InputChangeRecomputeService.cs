using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace vos.Service.Shared.Subscriptions;

/// <summary>What a compute service needs to keep its results current: the inputs it reads off a
/// subject, and how to recompute one.</summary>
/// <param name="ServiceName">Names the service in log lines.</param>
/// <param name="InputProperties">Only these trigger a recompute. A service also writes its outputs onto
/// the subject it watches, so reacting to every change on that subject would recompute forever.</param>
public sealed record RecomputeInputs(
    string ServiceName,
    IReadOnlySet<string> InputProperties,
    Func<Guid, CancellationToken, Task> RecomputeAsync);

/// <summary>
/// Keeps a compute service's results current: watch each subject it has computed for, and recompute when
/// one of that service's inputs moves.
///
/// Watching the subject alone covers both ways an input moves, because Mycelium publishes a derived value
/// on the Thing that owns it: a param someone edited arrives as a property change on the subject, and a
/// roll-up whose members changed arrives as a property change on the subject too.
///
/// A derived value is published live-only and never enters the journal, so a resumed stream does not
/// replay one. After a reconnect every watched subject is recomputed rather than trusted.
/// </summary>
public sealed class InputChangeRecomputeService : IHostedService
{
    private static readonly int[] BackoffMilliseconds = { 0, 1000, 2000, 5000, 10000 };

    private readonly ISubscriptionClient _subscriptions;
    private readonly RecomputeInputs _inputs;
    private readonly IHostEnvironment _environment;
    private readonly ILogger _logger;
    private readonly HashSet<Guid> _watched = new();

    private Guid _subscriptionId;
    private CancellationTokenSource? _cancellation;

    public InputChangeRecomputeService(
        ISubscriptionClient subscriptions, RecomputeInputs inputs, IHostEnvironment environment, ILogger logger)
    {
        _subscriptions = subscriptions;
        _inputs = inputs;
        _environment = environment;
        _logger = logger;
    }

    /// <summary>Start following a subject. Called when the service computes for one, so the set grows from
    /// the dispatches the service already receives rather than from a discovery rule of its own.</summary>
    public void Watch(Guid subjectId)
    {
        lock (_watched)
            if (!_watched.Add(subjectId)) return;

        if (_subscriptionId != Guid.Empty)
            _ = AddToMembershipAsync(subjectId);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Tests run against a synthetic Mycelium URL — don't open a real subscription.
        if (_environment.IsEnvironment("Testing")) return Task.CompletedTask;

        _subscriptions.Reconnected += OnReconnected;
        _cancellation = new CancellationTokenSource();
        _ = RunAsync(_cancellation.Token); // long-running; not tied to StartAsync's token
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _subscriptions.Reconnected -= OnReconnected;
        _cancellation?.Cancel();
        if (_subscriptionId == Guid.Empty) return;

        try { await _subscriptions.UnsubscribeAsync(_subscriptionId, cancellationToken); }
        catch (Exception ex) { _logger.LogDebug(ex, "{Service}: unsubscribe on shutdown failed", _inputs.ServiceName); }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var subscription = await SubscribeWithRetryAsync(cancellationToken);
        if (subscription is null) return;

        _subscriptionId = subscription.SubscriptionId;
        foreach (var subjectId in Snapshot())
            await AddToMembershipAsync(subjectId);

        _logger.LogInformation("{Service} is following its inputs on Mycelium ({SubscriptionId}) from {Watermark}",
            _inputs.ServiceName, _subscriptionId, subscription.Watermark);

        try
        {
            await foreach (var change in _subscriptions.StreamAsync(_subscriptionId, subscription.Watermark, cancellationToken))
            {
                if (!change.IsPropertyChange || change.PropertyName is null) continue;
                if (!_inputs.InputProperties.Contains(change.PropertyName)) continue;

                bool watching;
                lock (_watched) watching = _watched.Contains(change.EntityId);
                if (watching) await RecomputeAsync(change.EntityId, change.PropertyName, cancellationToken);
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    private async Task<SubscribeResult?> SubscribeWithRetryAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; !cancellationToken.IsCancellationRequested; attempt++)
        {
            try { return await _subscriptions.SubscribeAsync(new SubscriptionSelector(), cancellationToken); }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{Service}: subscribe failed (attempt {Attempt}); retrying",
                    _inputs.ServiceName, attempt + 1);
                try { await Task.Delay(BackoffMilliseconds[Math.Min(attempt, BackoffMilliseconds.Length - 1)], cancellationToken); }
                catch (OperationCanceledException) { return null; }
            }
        }
        return null;
    }

    private void OnReconnected()
    {
        var subjects = Snapshot();
        _logger.LogInformation(
            "{Service} resumed its Mycelium stream and is recomputing {Count} subject(s): a derived value is "
            + "published live-only, so any that moved while the stream was down was not replayed",
            _inputs.ServiceName, subjects.Count);

        var cancellationToken = _cancellation?.Token ?? CancellationToken.None;
        foreach (var subjectId in subjects)
            _ = RecomputeAsync(subjectId, "reconnect", cancellationToken);
    }

    private async Task RecomputeAsync(Guid subjectId, string reason, CancellationToken cancellationToken)
    {
        try { await _inputs.RecomputeAsync(subjectId, cancellationToken); }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Service}: recompute of {SubjectId} after '{Reason}' failed",
                _inputs.ServiceName, subjectId, reason);
        }
    }

    private async Task AddToMembershipAsync(Guid subjectId)
    {
        try
        {
            await _subscriptions.AddObjectsAsync(
                _subscriptionId, new SubscriptionSelector { Ids = new List<Guid> { subjectId } },
                _cancellation?.Token ?? CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Service}: could not follow {SubjectId}; its results will go stale",
                _inputs.ServiceName, subjectId);
        }
    }

    private List<Guid> Snapshot()
    {
        lock (_watched) return _watched.ToList();
    }
}
