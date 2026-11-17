using vos.Service.Metabolism.Models;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Metabolism.Services;

// Owns Metabolism's single Mycelium subscription. Subscribes at startup,
// streams relationship-property changes into the engine, and keeps the subscription's
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
        SubscribeResult? sub = null;
        for (var attempt = 0; !ct.IsCancellationRequested; attempt++)
        {
            try { sub = await _subscriptions.SubscribeAsync(new SubscriptionSelector { IncludeSnapshot = false }, ct); break; }
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
            await AddMembershipAsync(entry.Config, ct);

        _logger.LogInformation("Metabolism subscribed to Mycelium ({SubId}); streaming from {Watermark}",
            _subscriptionId, sub.Watermark);

        try
        {
            await foreach (var change in _subscriptions.StreamAsync(_subscriptionId, sub.Watermark, ct))
                if (change.IsRelationshipPropertyChange && change.PropertyName is not null)
                    _engine.UpdateProperty(change.EntityId.ToString(), change.PropertyName, change.Value);
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    private void OnRelationshipRegistered(SimulationConfig simulation)
        => _ = AddMembershipAsync(simulation, _cts?.Token ?? CancellationToken.None);

    private void OnRelationshipCancelled(SimulationConfig simulation)
    {
        if (_subscriptionId == Guid.Empty) return;
        _ = RemoveMembershipAsync(simulation);
    }

    // The broker resolves Ids against Things only, so the relationship is reached from its subject with a
    // relationship rule. The subject comes with it and its own changes travel too; the stream hands the
    // engine relationship changes alone.
    private async Task AddMembershipAsync(SimulationConfig simulation, CancellationToken ct)
    {
        if (_subscriptionId == Guid.Empty || !Guid.TryParse(simulation.SubjectId, out var subjectId)) return;
        var selector = new SubscriptionSelector
        {
            Ids = new() { subjectId },
            IncludeIsAncestors = false,
            IncludeRelationships = true,
            Relationships = new() { new RelationshipRule { Direction = "outgoing" } },
            IncludeSnapshot = false,
        };
        try { await _subscriptions.AddObjectsAsync(_subscriptionId, selector, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to add {RelId} to subscription membership", simulation.RelationshipId); }
    }

    private async Task RemoveMembershipAsync(SimulationConfig simulation)
    {
        var leaving = new List<Guid>();
        if (Guid.TryParse(simulation.RelationshipId, out var relationshipId)) leaving.Add(relationshipId);
        if (Guid.TryParse(simulation.SubjectId, out var subjectId) && !AnotherSimulationShares(simulation))
            leaving.Add(subjectId);
        if (leaving.Count == 0) return;

        try { await _subscriptions.RemoveObjectsAsync(_subscriptionId, leaving, _cts?.Token ?? CancellationToken.None); }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to remove {RelId} from subscription membership", simulation.RelationshipId); }
    }

    private bool AnotherSimulationShares(SimulationConfig simulation) =>
        _engine.GetAll().Any(entry =>
            entry.Config.SubjectId == simulation.SubjectId && entry.Config.RelationshipId != simulation.RelationshipId);
}
