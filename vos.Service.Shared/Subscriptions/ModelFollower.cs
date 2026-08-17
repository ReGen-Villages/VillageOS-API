using Microsoft.Extensions.Logging;

namespace vos.Service.Shared.Subscriptions;

/// <summary>
/// One project's half of a compute service: the subjects it has computed for in that model, the
/// subscription carrying that model's changes, and the bearer both are held open with.
///
/// The bearer is replaced before it expires rather than when a call fails. A subscription can sit quiet
/// for longer than a token lives, and by the time the stream dropped there would be no valid token left
/// to ask for a replacement with.
/// </summary>
internal sealed class ModelFollower
{
    private static readonly int[] BackoffMilliseconds = { 0, 1000, 2000, 5000, 10000 };

    private readonly IServiceTokenExchange _tokenExchange;
    private readonly RecomputeInputs _inputs;
    private readonly ILogger _logger;
    private readonly TimeSpan _replacementLeadTime;
    private readonly TimeSpan _replacementCheckInterval;
    private readonly ISubscriptionClient _subscriptions;

    private readonly HashSet<Guid> _watched = new();
    private readonly object _watchedLock = new();

    private ModelScopedBearer _bearer;
    private Guid _subscriptionId;
    private CancellationTokenSource? _cancellation;

    public ModelFollower(
        ModelScopedBearer seed, SubscriptionClientFactory subscriptionClientFor,
        IServiceTokenExchange tokenExchange, RecomputeInputs inputs, ILogger logger,
        TimeSpan replacementLeadTime, TimeSpan replacementCheckInterval)
    {
        _bearer = seed;
        _tokenExchange = tokenExchange;
        _inputs = inputs;
        _logger = logger;
        _replacementLeadTime = replacementLeadTime;
        _replacementCheckInterval = replacementCheckInterval;
        _subscriptions = subscriptionClientFor(CurrentTokenAsync);
        _subscriptions.Reconnected += OnReconnected;
    }

    public Guid ModelId => _bearer.ModelId;

    public void Watch(Guid subjectId)
    {
        lock (_watchedLock)
            if (!_watched.Add(subjectId)) return;

        if (_subscriptionId != Guid.Empty)
            _ = AddToMembershipAsync(subjectId);
    }

    public void Start(CancellationToken cancellationToken)
    {
        if (_cancellation != null) return;

        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = FollowAsync(_cancellation.Token);
        _ = ReplaceTokenBeforeItExpiresAsync(_cancellation.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _subscriptions.Reconnected -= OnReconnected;
        _cancellation?.Cancel();
        if (_subscriptionId == Guid.Empty) return;

        try { await _subscriptions.UnsubscribeAsync(_subscriptionId, cancellationToken); }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "{Service}: unsubscribe on shutdown failed for model {ModelId}",
                _inputs.ServiceName, ModelId);
        }
    }

    /// <summary>The bearer every call this follower makes is signed with. Asked for per call so a
    /// replacement takes effect without rebuilding the client.</summary>
    private Task<string?> CurrentTokenAsync() => Task.FromResult<string?>(Volatile.Read(ref _bearer).Token);

    private async Task FollowAsync(CancellationToken cancellationToken)
    {
        await ExtendTokenAsync(cancellationToken);

        var subscription = await SubscribeWithRetryAsync(cancellationToken);
        if (subscription is null) return;

        _subscriptionId = subscription.SubscriptionId;
        foreach (var subjectId in Snapshot())
            await AddToMembershipAsync(subjectId);

        _logger.LogInformation(
            "{Service} is following its inputs in model {ModelId} on Mycelium ({SubscriptionId}) from {Watermark}",
            _inputs.ServiceName, ModelId, _subscriptionId, subscription.Watermark);

        try
        {
            await foreach (var change in _subscriptions.StreamAsync(_subscriptionId, subscription.Watermark, cancellationToken))
            {
                if (!change.IsPropertyChange || change.PropertyName is null) continue;
                if (!_inputs.InputProperties.Contains(change.PropertyName)) continue;

                bool watching;
                lock (_watchedLock) watching = _watched.Contains(change.EntityId);
                if (watching) await RecomputeAsync(change.EntityId, change.PropertyName, cancellationToken);
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    /// <summary>Trade the bearer this follower was seeded with — typically the one that arrived on a
    /// /handle call, which expires in minutes — for one that outlasts the subscription.</summary>
    private async Task ExtendTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            var extended = await _tokenExchange.ExchangeAsync(Volatile.Read(ref _bearer).Token, cancellationToken);
            if (extended is null)
            {
                _logger.LogWarning(
                    "{Service}: could not extend its token for model {ModelId}; it will follow that model only until the token it has expires",
                    _inputs.ServiceName, ModelId);
                return;
            }

            Volatile.Write(ref _bearer, extended);
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    private async Task ReplaceTokenBeforeItExpiresAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try { await Task.Delay(_replacementCheckInterval, cancellationToken); }
            catch (OperationCanceledException) { return; }

            if (Volatile.Read(ref _bearer).IsDueForReplacement(DateTimeOffset.UtcNow, _replacementLeadTime))
                await ExtendTokenAsync(cancellationToken);
        }
    }

    private async Task<SubscribeResult?> SubscribeWithRetryAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; !cancellationToken.IsCancellationRequested; attempt++)
        {
            try { return await _subscriptions.SubscribeAsync(new SubscriptionSelector(), cancellationToken); }
            catch (OperationCanceledException) { return null; }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "{Service}: subscribe to model {ModelId} failed (attempt {Attempt}); retrying",
                    _inputs.ServiceName, ModelId, attempt + 1);
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
            "{Service} resumed its Mycelium stream for model {ModelId} and is recomputing {Count} subject(s): a derived "
            + "value is published live-only, so any that moved while the stream was down was not replayed",
            _inputs.ServiceName, ModelId, subjects.Count);

        var cancellationToken = _cancellation?.Token ?? CancellationToken.None;
        foreach (var subjectId in subjects)
            _ = RecomputeAsync(subjectId, "reconnect", cancellationToken);
    }

    /// <summary>Every recompute runs under this model's token, so a handler writes its results back into the
    /// project the subject belongs to without knowing there is more than one.</summary>
    private Task RecomputeAsync(Guid subjectId, string reason, CancellationToken cancellationToken) =>
        MyceliumModelToken.ActingForAsync(Volatile.Read(ref _bearer).Token, async () =>
        {
            try { await _inputs.RecomputeAsync(subjectId, cancellationToken); }
            catch (OperationCanceledException) { /* shutting down */ }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "{Service}: recompute of {SubjectId} in model {ModelId} after '{Reason}' failed",
                    _inputs.ServiceName, subjectId, ModelId, reason);
            }
        });

    private async Task AddToMembershipAsync(Guid subjectId)
    {
        try
        {
            await _subscriptions.AddObjectsAsync(
                _subscriptionId, new SubscriptionSelector { Ids = new List<Guid> { subjectId } },
                _cancellation?.Token ?? CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "{Service}: could not follow {SubjectId} in model {ModelId}; its results will go stale",
                _inputs.ServiceName, subjectId, ModelId);
        }
    }

    private List<Guid> Snapshot()
    {
        lock (_watchedLock) return _watched.ToList();
    }
}
