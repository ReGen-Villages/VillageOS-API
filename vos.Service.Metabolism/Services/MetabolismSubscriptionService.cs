using System.Text.Json;
using vos.Service.Metabolism.Models;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Metabolism.Services;

// Owns Metabolism's single Mycelium subscription. Subscribes at startup, and again when the broker no longer holds
// the subscription, streams relationship-property changes into the engine, and keeps the subscription's
// membership in step with the engine's simulations — adding a relationship through its subject on
// Register and removing both on Cancel. An open stream is also the
// service's liveness signal to Mycelium.
public sealed class MetabolismSubscriptionService : IHostedService
{
    private static readonly int[] Backoff = { 0, 1000, 2000, 5000, 10000 };

    private readonly ISubscriptionClient _subscriptions;
    private readonly Metabolism _engine;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<MetabolismSubscriptionService> _logger;

    private volatile SubscribeResult? _subscription;
    private CancellationTokenSource? _cts;

    public MetabolismSubscriptionService(
        ISubscriptionClient subscriptions,
        Metabolism engine,
        IHostEnvironment environment,
        ILogger<MetabolismSubscriptionService> logger)
    {
        _subscriptions = subscriptions;
        _engine = engine;
        _environment = environment;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Tests give an address no Mycelium answers on, so no subscription is opened.
        if (_environment.IsEnvironment("Testing")) return Task.CompletedTask;

        _engine.RelationshipRegistered += OnRelationshipRegistered;
        _engine.RelationshipCancelled += OnRelationshipCancelled;
        _cts = new CancellationTokenSource();
        _ = RunAsync(_cts.Token); // long-running; not tied to StartAsync's token
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _engine.RelationshipRegistered -= OnRelationshipRegistered;
        _engine.RelationshipCancelled -= OnRelationshipCancelled;
        _cts?.Cancel();
        if (_subscription is { } subscription)
        {
            try { await _subscriptions.UnsubscribeAsync(subscription.SubscriptionId, cancellationToken); }
            catch (Exception ex) { _logger.LogDebug(ex, "Unsubscribe on shutdown failed"); }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (await SubscribeWithRetryAsync(ct) is { } subscription)
            await FollowUntilGoneAsync(subscription, ct);
    }

    private async Task<SubscribeResult?> SubscribeWithRetryAsync(CancellationToken ct)
    {
        for (var attempt = 0; !ct.IsCancellationRequested; attempt++)
        {
            try { return await _subscriptions.SubscribeAsync(new SubscriptionSelector { IncludeSnapshot = false }, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return null; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Metabolism subscribe failed (attempt {Attempt}); retrying", attempt + 1);
                try { await Task.Delay(Backoff[Math.Min(attempt, Backoff.Length - 1)], ct); }
                catch (OperationCanceledException) { return null; }
            }
        }
        return null;
    }

    private async Task FollowUntilGoneAsync(SubscribeResult subscription, CancellationToken ct)
    {
        _subscription = subscription;
        foreach (var entry in _engine.GetAll())
            await CatchUpAsync(subscription, entry.Config, ct);

        _logger.LogInformation("Metabolism subscribed to Mycelium ({SubId}); streaming from {Watermark}",
            subscription.SubscriptionId, subscription.Watermark);

        try
        {
            await foreach (var change in _subscriptions.StreamAsync(subscription.SubscriptionId, subscription.Watermark, ct))
                if (change.IsRelationshipPropertyChange && change.PropertyName is not null)
                    _engine.UpdateProperty(change.EntityId.ToString(), change.PropertyName, change.Value);
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    // A simulation registered before the subscription was opened may have been edited since, and no stream carried it.
    private async Task CatchUpAsync(SubscribeResult subscription, SimulationConfig simulation, CancellationToken ct)
    {
        if (await AddMembershipAsync(subscription, simulation, includeSnapshot: true, ct) is not { } added) return;
        foreach (var relationship in added.Snapshot.Relationships.Where(held => held.Id.ToString() == simulation.RelationshipId))
            foreach (var (name, stated) in relationship.ValuesStated())
                if (stated.Value.ValueKind != JsonValueKind.Null)
                    _engine.UpdateProperty(simulation.RelationshipId, name, stated.Value);
    }

    private void OnRelationshipRegistered(SimulationConfig simulation)
    {
        if (_subscription is not { } subscription) return;
        _ = AddMembershipAsync(subscription, simulation, includeSnapshot: false, _cts?.Token ?? CancellationToken.None);
    }

    private void OnRelationshipCancelled(SimulationConfig simulation)
    {
        if (_subscription is not { } subscription) return;
        _ = RemoveMembershipAsync(subscription, simulation);
    }

    // The broker resolves Ids against Things only, so the relationship is reached from its subject with a
    // relationship rule. The subject comes with it and its own changes travel too; the stream hands the
    // engine relationship changes alone.
    private async Task<AddObjectsResult?> AddMembershipAsync(
        SubscribeResult subscription, SimulationConfig simulation, bool includeSnapshot, CancellationToken ct)
    {
        if (!Guid.TryParse(simulation.SubjectId, out var subjectId)) return null;
        var selector = new SubscriptionSelector
        {
            Ids = new() { subjectId },
            IncludeIsAncestors = false,
            IncludeRelationships = true,
            Relationships = new() { new RelationshipRule { Direction = "outgoing" } },
            IncludeSnapshot = includeSnapshot,
        };
        try { return await _subscriptions.AddObjectsAsync(subscription.SubscriptionId, selector, ct); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to add {RelId} to subscription membership", simulation.RelationshipId);
            return null;
        }
    }

    private async Task RemoveMembershipAsync(SubscribeResult subscription, SimulationConfig simulation)
    {
        var leaving = new List<Guid>();
        if (Guid.TryParse(simulation.RelationshipId, out var relationshipId)) leaving.Add(relationshipId);
        if (Guid.TryParse(simulation.SubjectId, out var subjectId) && !AnotherSimulationShares(simulation))
            leaving.Add(subjectId);
        if (leaving.Count == 0) return;

        try { await _subscriptions.RemoveObjectsAsync(subscription.SubscriptionId, leaving, _cts?.Token ?? CancellationToken.None); }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to remove {RelId} from subscription membership", simulation.RelationshipId); }
    }

    private bool AnotherSimulationShares(SimulationConfig simulation) =>
        _engine.GetAll().Any(entry =>
            entry.Config.SubjectId == simulation.SubjectId && entry.Config.RelationshipId != simulation.RelationshipId);
}
