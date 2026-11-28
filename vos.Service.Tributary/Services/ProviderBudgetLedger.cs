using vos.Service.Tributary.Helpers;

namespace vos.Service.Tributary.Services;

public readonly record struct BudgetSlot(bool Taken, TimeSpan TryAgainAfter);

public sealed record BudgetReservation(BudgetSlot Slot, DateTimeOffset At);

public sealed class ProviderBudgetLedger(string directory, TimeProvider clock)
{
    public BudgetReservation Reserve(IReadOnlyList<ProviderBudget> budgets, TimeSpan mostWait) =>
        new(new BudgetSlot(true, TimeSpan.Zero), clock.GetUtcNow());

    public void Keep(BudgetReservation reservation)
    {
    }

    public Task<BudgetSlot> TakeAsync(IReadOnlyList<ProviderBudget> budgets, TimeSpan mostWait, CancellationToken cancellationToken) =>
        Task.FromResult(new BudgetSlot(true, TimeSpan.Zero));
}
