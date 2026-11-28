using System.Text.Json;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Tributary.Helpers;

public sealed record ProviderBudget(Guid Id, string Name, int RequestCount, TimeSpan Window);

public sealed record ProviderBudgetsRead(IReadOnlyList<ProviderBudget> Budgets, string? UnreadableBudgetName);

// Found through the `licence` and `holding` roles, so a model may call both predicates whatever it likes.
public static class ProviderBudgets
{
    public const string RequestCountProperty = "requestCount";
    public const string WindowSecondsProperty = "windowSeconds";

    public static ProviderBudgetsRead Resolve(SnapshotDocument snapshot, Guid endpointId)
    {
        var licencePredicates = BoundIds(snapshot, LandIntakeRoles.Licence);
        var holdingPredicates = BoundIds(snapshot, LandIntakeRoles.Holding);
        if (licencePredicates.Count == 0 || holdingPredicates.Count == 0) return new([], null);

        var thingsById = snapshot.Things.ToDictionary(thing => thing.Id);
        var licences = Targets(snapshot, EndpointKindResolver.ChainFrom(snapshot, endpointId, thingsById), licencePredicates);
        var budgets = new List<ProviderBudget>();
        foreach (var heldId in Targets(snapshot, licences, holdingPredicates))
        {
            if (!thingsById.TryGetValue(heldId, out var held)
                || (!States(held, RequestCountProperty) && !States(held, WindowSecondsProperty)))
                continue;

            var name = held.Name ?? heldId.ToString();
            var count = held.StatedWholeNumber(RequestCountProperty);
            var seconds = held.StatedWholeNumber(WindowSecondsProperty);
            if (count is not (> 0 and <= int.MaxValue) || seconds is not > 0)
                return new([], name);
            budgets.Add(new ProviderBudget(heldId, name, (int)count.Value, TimeSpan.FromSeconds(seconds.Value)));
        }
        return new(budgets, null);
    }

    private static bool States(SnapshotThing thing, string property) =>
        thing.StatedValue(property) is { Value.ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) };

    private static HashSet<Guid> BoundIds(SnapshotDocument snapshot, Role role) =>
        [.. snapshot.Bound(role).Select(thing => thing.Id)];

    private static List<Guid> Targets(SnapshotDocument snapshot, IEnumerable<Guid> subjects, IReadOnlySet<Guid> predicates)
    {
        var from = subjects.ToHashSet();
        return [.. snapshot.Relationships
            .Where(edge => from.Contains(edge.SubjectId) && predicates.Contains(edge.PredicateId))
            .Select(edge => edge.TargetId)
            .Distinct()];
    }
}
