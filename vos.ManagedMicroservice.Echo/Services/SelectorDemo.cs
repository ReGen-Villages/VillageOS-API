using vos.ManagedMicroservice.Shared.Subscriptions;

namespace vos.ManagedMicroservice.Echo.Services;

/// <summary>
/// Worked example of the snapshot <b>selector</b> — the mechanism that replaced launch-time object
/// IDs (the retired ServiceArgs ID template). Instead of being handed IDs at startup, a handler
/// subscribes with a selector describing the <i>slice</i> of the model it needs, receives that exact
/// closure as a snapshot, then (normally) follows the SSE stream. This demo subscribes, summarises
/// the resolved closure, and unsubscribes — so it shows the selector without leaving a live stream.
///
/// See docs/MICROSERVICE_CONTRACT.md § "Selecting a slice" for the full cookbook of selector fields.
/// </summary>
public sealed class SelectorDemo
{
    private readonly ISubscriptionClient _subscriptions;

    public SelectorDemo(ISubscriptionClient subscriptions) => _subscriptions = subscriptions;

    public async Task<SelectorDemoResult> RunAsync(SubscriptionSelector selector, CancellationToken ct = default)
    {
        var sub = await _subscriptions.SubscribeAsync(selector, ct);
        var result = new SelectorDemoResult(
            sub.SubscriptionId,
            sub.Watermark,
            sub.Snapshot.Things.Count,
            sub.Snapshot.Relationships.Count,
            sub.Snapshot.Things.Select(t => t.Name ?? t.Id.ToString()).Take(25).ToList());

        // A real handler would now follow StreamAsync(sub.SubscriptionId, sub.Watermark). This demo
        // only illustrates the selector, so it releases the subscription instead.
        await _subscriptions.UnsubscribeAsync(sub.SubscriptionId, ct);
        return result;
    }

    /// <summary>
    /// A representative "slice" selector: every Thing of <paramref name="type"/> plus its depth-1
    /// neighbours along <paramref name="predicate"/> and the connecting relationships. This is the
    /// pattern that replaced launch-time IDs — ask for the slice by shape, not by id.
    /// </summary>
    public static SubscriptionSelector SliceByTypeAndTraverse(string type, string predicate) => new()
    {
        Types = new List<string> { type },
        Traverse = new List<TraverseRule> { new() { Predicate = predicate, Direction = "outgoing", Depth = 1 } },
        IncludeRelationships = true,
    };
}

public readonly record struct SelectorDemoResult(
    Guid SubscriptionId, long Watermark, int Things, int Relationships, List<string> ThingNames);

/// <summary>Optional body for <c>POST /demo/subscribe</c>; omit to use the default Battery/powers slice.</summary>
public sealed record SelectorDemoRequest(string? Type, string? Predicate);
