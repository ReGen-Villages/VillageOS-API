using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Tests;

/// <summary>A scoped read that answers with a model a test built, and records what it was asked for and
/// whether the subscription was released.</summary>
public sealed class StubSubscriptions(SnapshotDocument snapshot) : ISubscriptionClient
{
    public SubscriptionSelector? AskedFor { get; private set; }

    public int Released { get; private set; }

    public bool FailRelease { get; set; }

    public Task<SubscribeResult> SubscribeAsync(SubscriptionSelector selector, CancellationToken ct = default)
    {
        AskedFor = selector;
        return Task.FromResult(new SubscribeResult(Guid.NewGuid(), 0, snapshot));
    }

    public Task UnsubscribeAsync(Guid subscriptionId, CancellationToken ct = default)
    {
        if (FailRelease) throw new HttpRequestException("mycelium is already gone");
        Released++;
        return Task.CompletedTask;
    }

    public Task<AddObjectsResult> AddObjectsAsync(Guid id, SubscriptionSelector selector, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task RemoveObjectsAsync(Guid id, IEnumerable<Guid> objectIds, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<ModelChangeEvent> StreamAsync(Guid id, long from, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public event Action? Reconnected { add { } remove { } }
}
