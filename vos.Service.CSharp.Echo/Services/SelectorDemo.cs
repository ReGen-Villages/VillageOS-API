using vos.Service.Shared.Subscriptions;

namespace vos.Service.CSharp.Echo.Services;

// Worked example of the snapshot selector that replaced launch-time object IDs
// (docs/SERVICE_CONTRACT.md § "Selecting a slice").
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

        await _subscriptions.UnsubscribeAsync(sub.SubscriptionId, ct); // demo shows the selector, not streaming
        return result;
    }

    // Every Thing of type plus its depth-1 predicate neighbours.
    public static SubscriptionSelector SliceByTypeAndTraverse(string type, string predicate) => new()
    {
        Types = new List<string> { type },
        Traverse = new List<TraverseRule> { new() { Predicate = predicate, Direction = "outgoing", Depth = 1 } },
        IncludeRelationships = true,
    };
}

public readonly record struct SelectorDemoResult(
    Guid SubscriptionId, long Watermark, int Things, int Relationships, List<string> ThingNames);

public sealed record SelectorDemoRequest(string? Type, string? Predicate);
