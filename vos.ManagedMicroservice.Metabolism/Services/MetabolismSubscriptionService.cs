using Microsoft.Extensions.Hosting;
using vos.ManagedMicroservice.Shared.Subscriptions;

namespace vos.ManagedMicroservice.Metabolism.Services;

/// <summary>
/// Owns Metabolism's single Mycelium subscription (Phase 5c, #5558). Subscribes at startup,
/// streams relationship-property changes into the engine, and keeps the subscription's
/// membership in step with the engine's simulations — adding a relationship on Register and
/// removing it on Cancel. An open stream is also the
/// service's liveness signal to Mycelium).
/// </summary>
public sealed class MetabolismSubscriptionService : IHostedService
{
    private static readonly int[] Backoff = { 0, 1000, 2000, 5000, 10000 };

    private readonly ISubscriptionClient _subscriptions;
    private readonly Metabolism _engine;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<MetabolismSubscriptionService> _logger;

    private Guid _subscriptionId;
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
        // Tests run against a synthetic Mycelium URL — don't open a real subscription.
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
        if (_subscriptionId != Guid.Empty)
        {
            try { await _subscriptions.UnsubscribeAsync(_subscriptionId, cancellationToken); }
            catch (Exception ex) { _logger.LogDebug(ex, "Unsubscribe on shutdown failed"); }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        // Subscribe (empty closure — membership grows as simulations register) with retry.
        SubscribeResult? sub = null;
        for (var attempt = 0; !ct.IsCancellationRequested; attempt++)
        {
            try { sub = await _subscriptions.SubscribeAsync(new SubscriptionSelector(), ct); break; }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Metabolism subscribe failed (attempt {Attempt}); retrying", attempt + 1);
                try { await Task.Delay(Backoff[Math.Min(attempt, Backoff.Length - 1)], ct); }
                catch (OperationCanceledException) { return; }
            }
        }
        if (sub is null) return;
        _subscriptionId = sub.SubscriptionId;

        // Catch up: cover relationships registered before the subscription existed.
        foreach (var entry in _engine.GetAll())
            await AddMembershipAsync(entry.Config.RelationshipId, ct);

        _logger.LogInformation("Metabolism subscribed to Mycelium ({SubId}); streaming from {Watermark}",
            _subscriptionId, sub.Watermark);

        try
        {
            await foreach (var change in _subscriptions.StreamAsync(_subscriptionId, sub.Watermark, ct))
                if (change.IsPropertyChange && change.PropertyName is not null)
                    _engine.UpdateProperty(change.EntityId.ToString(), change.PropertyName, change.Value);
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    private void OnRelationshipRegistered(string relationshipId)
        => _ = AddMembershipAsync(relationshipId, _cts?.Token ?? CancellationToken.None);

    private void OnRelationshipCancelled(string relationshipId)
    {
        if (_subscriptionId == Guid.Empty || !Guid.TryParse(relationshipId, out var id)) return;
        _ = RemoveMembershipAsync(id);
    }

    private async Task AddMembershipAsync(string relationshipId, CancellationToken ct)
    {
        if (_subscriptionId == Guid.Empty || !Guid.TryParse(relationshipId, out var id)) return;
        try { await _subscriptions.AddObjectsAsync(_subscriptionId, new SubscriptionSelector { Ids = new() { id } }, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to add {RelId} to subscription membership", relationshipId); }
    }

    private async Task RemoveMembershipAsync(Guid id)
    {
        try { await _subscriptions.RemoveObjectsAsync(_subscriptionId, new[] { id }, _cts?.Token ?? CancellationToken.None); }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to remove {RelId} from subscription membership", id); }
    }
}
