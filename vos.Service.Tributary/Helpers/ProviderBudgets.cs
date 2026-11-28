using vos.Service.Shared.Subscriptions;

namespace vos.Service.Tributary.Helpers;

public sealed record ProviderBudget(Guid Id, string Name, int RequestCount, TimeSpan Window);

public sealed record ProviderBudgetsRead(IReadOnlyList<ProviderBudget> Budgets, string? Unreadable);

public static class ProviderBudgets
{
    public static ProviderBudgetsRead Resolve(SnapshotDocument snapshot, Guid endpointId) => new([], null);
}
