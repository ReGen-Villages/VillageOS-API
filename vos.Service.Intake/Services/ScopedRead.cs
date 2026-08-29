using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Services;

/// <summary>A read of the model that lasts one call.</summary>
internal static class ScopedRead
{
    /// <summary>Subscribe, read, release. A release that fails must not lose a read that succeeded, and
    /// the broker reaps what a caller leaves behind — so the failure is logged where whoever runs the
    /// deployment reads it and the answer still goes back.</summary>
    public static async Task<T> ReadAsync<T>(
        this ISubscriptionClient subscriptions,
        SubscriptionSelector selector,
        Func<SnapshotDocument, T> read,
        ILogger logger,
        CancellationToken cancellation)
    {
        var subscribed = await subscriptions.SubscribeAsync(selector, cancellation);
        try
        {
            return read(subscribed.Snapshot);
        }
        finally
        {
            try
            {
                await subscriptions.UnsubscribeAsync(subscribed.SubscriptionId, cancellation);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Intake could not release the subscription {SubscriptionId}",
                    subscribed.SubscriptionId);
            }
        }
    }
}
