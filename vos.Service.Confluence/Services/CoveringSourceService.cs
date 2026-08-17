using Microsoft.Extensions.Logging;
using vos.Service.Confluence.Helpers;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Confluence.Services;

// Which sources cover a site, read from the model in one scoped snapshot.
//
// Null means the read failed, never that the site is covered by nothing. Collapsing the two would
// report a gateway outage as "no source covers this site", which is the silently-short list this
// whole path exists to prevent.
public sealed class CoveringSourceService
{
    private readonly ISubscriptionClient _subscriptions;
    private readonly ILogger<CoveringSourceService> _logger;

    public CoveringSourceService(ISubscriptionClient subscriptions, ILogger<CoveringSourceService> logger)
    {
        _subscriptions = subscriptions;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CoveringSource>?> ForSiteAsync(Guid siteId, CancellationToken cancellationToken)
    {
        SubscribeResult subscribed;
        try
        {
            subscribed = await _subscriptions.SubscribeAsync(
                CoveringSourceResolver.SelectorFor(siteId), cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to read which sources cover site {SiteId}", siteId);
            return null;
        }

        try
        {
            return CoveringSourceResolver.Resolve(subscribed.Snapshot, siteId);
        }
        finally
        {
            try
            {
                await _subscriptions.UnsubscribeAsync(subscribed.SubscriptionId, cancellationToken);
            }
            catch (Exception exception)
            {
                // The read already succeeded; failing to release the subscription must not lose it.
                _logger.LogWarning(exception, "Failed to release the coverage subscription {SubscriptionId}",
                    subscribed.SubscriptionId);
            }
        }
    }
}
