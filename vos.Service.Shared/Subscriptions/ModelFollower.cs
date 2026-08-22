using Microsoft.Extensions.Logging;

namespace vos.Service.Shared.Subscriptions;

/// <summary>
/// What a compute service holds for one project: the subjects it has computed for in that model, the
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

    // A watched Thing names every subject whose result its changes invalidate, and each subject names
    // what it reads, so re-registering a subject can stop following what it no longer reads. A service
    // that reads only the Thing it computes has one entry mapping that Thing to itself.
    private readonly Dictionary<Guid, HashSet<Guid>> _subjectsByWatched = new();
    private readonly Dictionary<Guid, HashSet<Guid>> _readBySubject = new();
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

    /// <summary>Follow a subject and every Thing its result is computed from. Re-registering replaces
    /// what the subject reads, because a planner can add or remove one of them.</summary>
    public void Watch(Guid subjectId, IReadOnlyCollection<Guid> readsFrom)
    {
        List<Guid> joined, abandoned;
        lock (_watchedLock)
        {
            var reads = new HashSet<Guid>(readsFrom) { subjectId };
            joined = reads.Where(thing => !_subjectsByWatched.ContainsKey(thing)).ToList();
            abandoned = ReleaseWhatTheSubjectNoLongerReads(subjectId, reads);

            foreach (var thing in reads)
            {
                if (!_subjectsByWatched.TryGetValue(thing, out var subjects))
                    _subjectsByWatched[thing] = subjects = new HashSet<Guid>();
                subjects.Add(subjectId);
            }

            _readBySubject[subjectId] = reads;
        }

        if (_subscriptionId == Guid.Empty) return;
        foreach (var thing in joined)
            _ = AddToMembershipAsync(thing);
        if (abandoned.Count > 0)
            _ = RemoveFromMembershipAsync(abandoned);
    }

    /// <summary>Drops the subject from everything it has stopped reading, and answers with the Things no
    /// subject reads any more. Held under the caller's lock.</summary>
    private List<Guid> ReleaseWhatTheSubjectNoLongerReads(Guid subjectId, HashSet<Guid> reads)
    {
        var abandoned = new List<Guid>();
        if (!_readBySubject.TryGetValue(subjectId, out var previous)) return abandoned;

        foreach (var thing in previous.Where(thing => !reads.Contains(thing)))
            if (_subjectsByWatched.TryGetValue(thing, out var subjects)
                && subjects.Remove(subjectId) && subjects.Count == 0)
            {
                _subjectsByWatched.Remove(thing);
                abandoned.Add(thing);
            }

        return abandoned;
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
        foreach (var thing in WatchedThings())
            await AddToMembershipAsync(thing);

        _logger.LogInformation(
            "{Service} is following its inputs in model {ModelId} on Mycelium ({SubscriptionId}) from {Watermark}",
            _inputs.ServiceName, ModelId, _subscriptionId, subscription.Watermark);

        try
        {
            await foreach (var change in _subscriptions.StreamAsync(_subscriptionId, subscription.Watermark, cancellationToken))
            {
                if (!change.IsPropertyChange || change.PropertyName is null) continue;
                if (!_inputs.InputProperties().Contains(change.PropertyName)) continue;

                List<Guid> subjects;
                lock (_watchedLock)
                    subjects = _subjectsByWatched.TryGetValue(change.EntityId, out var found)
                        ? found.ToList()
                        : new List<Guid>();

                foreach (var subjectId in subjects)
                    await RecomputeAsync(subjectId, change.PropertyName, cancellationToken);
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
        var subjects = Subjects();
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

    /// <summary>Every Thing whose changes have to reach this follower, which is what the subscription
    /// covers. Wider than the subjects: a subject is recomputed, the Things it reads only report.</summary>
    /// <summary>A Thing no subject reads any more keeps arriving on the stream until the subscription is
    /// told to drop it, and a service that re-registers per recompute would otherwise grow its membership
    /// for the lifetime of the model.</summary>
    private async Task RemoveFromMembershipAsync(IReadOnlyCollection<Guid> things)
    {
        try
        {
            await _subscriptions.RemoveObjectsAsync(
                _subscriptionId, things, _cancellation?.Token ?? CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception,
                "{Service}: could not stop following {Count} Thing(s) in model {ModelId}; their changes will be read and ignored",
                _inputs.ServiceName, things.Count, ModelId);
        }
    }

    private List<Guid> WatchedThings()
    {
        lock (_watchedLock) return _subjectsByWatched.Keys.ToList();
    }

    private List<Guid> Subjects()
    {
        lock (_watchedLock) return _readBySubject.Keys.ToList();
    }
}
