using Microsoft.Extensions.Logging;

namespace vos.Service.Shared.Subscriptions;

// What a compute service holds for one project: the subjects it has computed for in that model, the
// subscription carrying that model's changes, and the bearer both are held open with.
//
// The bearer is replaced before it expires rather than when a call fails. A subscription can sit quiet
// for longer than a token lives, and by the time the stream dropped there would be no valid token left
// to ask for a replacement with.
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
    //
    // A watched Thing's inputs resolve through its type chain, so each archetype above it names the watched
    // Things below it: an edit on a shared default recomputes every subject reading a Thing that `is` it.
    // The chain is learned from the add-objects answer, which carries the Things the broker registered.
    private readonly Dictionary<Guid, HashSet<Guid>> _subjectsByWatched = new();
    private readonly Dictionary<Guid, HashSet<Guid>> _readBySubject = new();
    private readonly Dictionary<Guid, HashSet<Guid>> _watchedThingsByAncestor = new();
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

    // Follow a subject and every Thing its result is computed from. Re-registering replaces
    // what the subject reads, because a planner can add or remove one of them.
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

    // Drops the subject from everything it has stopped reading, and answers with the Things no
    // subject reads any more. Held under the caller's lock.
    private List<Guid> ReleaseWhatTheSubjectNoLongerReads(Guid subjectId, HashSet<Guid> reads)
    {
        var abandoned = new List<Guid>();
        if (!_readBySubject.TryGetValue(subjectId, out var previous)) return abandoned;

        foreach (var thing in previous.Where(thing => !reads.Contains(thing)))
            if (_subjectsByWatched.TryGetValue(thing, out var subjects)
                && subjects.Remove(subjectId) && subjects.Count == 0)
            {
                _subjectsByWatched.Remove(thing);
                if (!_watchedThingsByAncestor.ContainsKey(thing)) abandoned.Add(thing);
                ReleaseTypeChainOf(thing, abandoned);
            }

        return abandoned;
    }

    // An archetype leaves with the last watched Thing below it, unless a subject reads the archetype itself.
    // Held under the caller's lock.
    private void ReleaseTypeChainOf(Guid watched, List<Guid> abandoned)
    {
        foreach (var (ancestor, below) in _watchedThingsByAncestor.ToList())
            if (below.Remove(watched) && below.Count == 0)
            {
                _watchedThingsByAncestor.Remove(ancestor);
                if (!_subjectsByWatched.ContainsKey(ancestor)) abandoned.Add(ancestor);
            }
    }

    // Held under the caller's lock.
    private void RecordTypeChainOf(Guid watched, IEnumerable<Guid> ancestors)
    {
        if (!_subjectsByWatched.ContainsKey(watched)) return;
        foreach (var ancestor in ancestors)
        {
            if (!_watchedThingsByAncestor.TryGetValue(ancestor, out var below))
                _watchedThingsByAncestor[ancestor] = below = new HashSet<Guid>();
            below.Add(watched);
        }
    }

    private List<Guid> SubjectsAffectedBy(Guid changedThing)
    {
        lock (_watchedLock)
        {
            var affected = new HashSet<Guid>();
            if (_subjectsByWatched.TryGetValue(changedThing, out var direct)) affected.UnionWith(direct);
            if (_watchedThingsByAncestor.TryGetValue(changedThing, out var below))
                foreach (var watched in below)
                    if (_subjectsByWatched.TryGetValue(watched, out var subjects)) affected.UnionWith(subjects);
            return affected.ToList();
        }
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

    // The bearer every call this follower makes is signed with. Asked for per call so a
    // replacement takes effect without rebuilding the client.
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

                foreach (var subjectId in SubjectsAffectedBy(change.EntityId))
                    await RecomputeAsync(subjectId, change.PropertyName, cancellationToken);
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    // Trade the bearer this follower was seeded with — typically the one that arrived on a
    // /handle call, which expires in minutes — for one that outlasts the subscription.
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
            try { return await _subscriptions.SubscribeAsync(new SubscriptionSelector { IncludeSnapshot = false }, cancellationToken); }
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

    // Every recompute runs under this model's token, so a handler writes its results back into the
    // project the subject belongs to without knowing there is more than one.
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

    // The Thing and its type chain, and none of its relationships: a change is mapped to a subject by the
    // Thing it arrived on, so a relationship in the membership would only ever be delivered and dropped.
    private async Task AddToMembershipAsync(Guid watched)
    {
        try
        {
            var added = await _subscriptions.AddObjectsAsync(
                _subscriptionId,
                new SubscriptionSelector
                {
                    Ids = new List<Guid> { watched },
                    IncludeIsAncestors = true,
                    IncludeRelationships = false,
                    IncludeSnapshot = true,
                },
                _cancellation?.Token ?? CancellationToken.None);

            lock (_watchedLock)
                RecordTypeChainOf(watched, added.Snapshot.Things.Select(thing => thing.Id).Where(id => id != watched));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "{Service}: could not follow {SubjectId} in model {ModelId}; its results will go stale",
                _inputs.ServiceName, watched, ModelId);
        }
    }

    // Every Thing whose changes have to reach this follower, which is what the subscription
    // covers. Wider than the subjects: a subject is recomputed, the Things it reads only report.
    // A Thing no subject reads any more keeps arriving on the stream until the subscription is
    // told to drop it, and a service that re-registers per recompute would otherwise grow its membership
    // for the lifetime of the model.
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
