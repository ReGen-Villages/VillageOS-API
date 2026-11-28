using System.Collections.Concurrent;
using System.Text.Json;
using vos.Service.Tributary.Helpers;

namespace vos.Service.Tributary.Services;

public readonly record struct BudgetSlot(bool Taken, TimeSpan TryAgainAfter);

public sealed record BudgetReservation(BudgetSlot Slot, DateTimeOffset At, IReadOnlyList<KeptWindow> Copies);

public sealed record KeptWindow(Guid BudgetId, long Version, IReadOnlyList<DateTimeOffset> Times);

// Keyed by the budget's identifier, so every registration and model reaching one budget spends one count.
public sealed class ProviderBudgetLedger(string directory, TimeProvider clock)
{
    // A restart forgets a window this short, which costs the provider at most one window of calls.
    private static readonly TimeSpan LongestWindowNotKept = TimeSpan.FromMinutes(1);

    private sealed class Window
    {
        public List<DateTimeOffset> Times { get; } = [];
        public long Version { get; set; }
    }

    private sealed class KeptFile
    {
        public Lock Gate { get; } = new();
        public long WrittenVersion { get; set; }
    }

    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, Window> _windows = [];
    private readonly ConcurrentDictionary<Guid, KeptFile> _kept = new();

    public async Task<BudgetSlot> TakeAsync(IReadOnlyList<ProviderBudget> budgets, TimeSpan mostWait, CancellationToken cancellationToken)
    {
        if (budgets.Count == 0) return new BudgetSlot(true, TimeSpan.Zero);

        var reservation = Reserve(budgets, mostWait);
        if (!reservation.Slot.Taken) return reservation.Slot;
        Keep(reservation);

        var wait = reservation.At - clock.GetUtcNow();
        if (wait <= TimeSpan.Zero) return reservation.Slot;
        try
        {
            await Task.Delay(wait, clock, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Keep(GiveBack(budgets, reservation.At));
            throw;
        }
        return reservation.Slot;
    }

    public BudgetReservation Reserve(IReadOnlyList<ProviderBudget> budgets, TimeSpan mostWait)
    {
        var readBack = budgets.Where(IsKept).Where(budget => !IsHeld(budget.Id)).DistinctBy(budget => budget.Id)
            .ToDictionary(budget => budget.Id, budget => ReadKept(budget.Id));

        lock (_lock)
        {
            var now = clock.GetUtcNow();
            var at = now;
            foreach (var budget in budgets)
            {
                var window = WindowOf(budget.Id, readBack);
                window.Times.RemoveAll(time => time <= now - budget.Window);
                if (EarliestSlot(window, budget) is var slot && slot > at) at = slot;
            }

            if (at - now > mostWait)
                return new BudgetReservation(new BudgetSlot(false, WholeSeconds(at - now)), at, []);

            foreach (var budget in budgets)
            {
                var window = _windows[budget.Id];
                window.Times.Add(at);
                window.Version++;
            }
            return new BudgetReservation(new BudgetSlot(true, TimeSpan.Zero), at, CopiesOf(budgets));
        }
    }

    // Copies are written after the ledger's lock is let go and can finish out of order, so an older one is skipped.
    public void Keep(BudgetReservation reservation)
    {
        foreach (var copy in reservation.Copies)
        {
            var file = _kept.GetOrAdd(copy.BudgetId, _ => new KeptFile());
            lock (file.Gate)
            {
                if (copy.Version <= file.WrittenVersion) continue;
                Directory.CreateDirectory(directory);
                var path = PathOf(copy.BudgetId);
                var written = path + ".new";
                File.WriteAllBytes(written, JsonSerializer.SerializeToUtf8Bytes(copy.Times.Select(time => time.ToUnixTimeMilliseconds())));
                File.Move(written, path, overwrite: true);
                file.WrittenVersion = copy.Version;
            }
        }
    }

    private BudgetReservation GiveBack(IReadOnlyList<ProviderBudget> budgets, DateTimeOffset at)
    {
        lock (_lock)
        {
            foreach (var budget in budgets)
            {
                var window = _windows[budget.Id];
                window.Times.Remove(at);
                window.Version++;
            }
            return new BudgetReservation(new BudgetSlot(false, TimeSpan.Zero), at, CopiesOf(budgets));
        }
    }

    private List<KeptWindow> CopiesOf(IReadOnlyList<ProviderBudget> budgets) =>
        [.. budgets.Where(IsKept).Select(budget => new KeptWindow(budget.Id, _windows[budget.Id].Version, [.. _windows[budget.Id].Times]))];

    // Never before a slot already handed out, so a window's times stay in order and its count-th newest is the one to leave.
    private static DateTimeOffset EarliestSlot(Window window, ProviderBudget budget)
    {
        var times = window.Times;
        if (times.Count == 0) return DateTimeOffset.MinValue;
        var afterTheLast = times[^1];
        return times.Count < budget.RequestCount ? afterTheLast : Later(afterTheLast, times[^budget.RequestCount] + budget.Window);
    }

    private static DateTimeOffset Later(DateTimeOffset first, DateTimeOffset second) => first > second ? first : second;

    private static TimeSpan WholeSeconds(TimeSpan wait) => TimeSpan.FromSeconds(Math.Ceiling(wait.TotalSeconds));

    private static bool IsKept(ProviderBudget budget) => budget.Window > LongestWindowNotKept;

    private bool IsHeld(Guid budgetId)
    {
        lock (_lock) return _windows.ContainsKey(budgetId);
    }

    private Window WindowOf(Guid budgetId, IReadOnlyDictionary<Guid, IReadOnlyList<DateTimeOffset>> readBack)
    {
        if (_windows.TryGetValue(budgetId, out var window)) return window;
        window = new Window();
        if (readBack.TryGetValue(budgetId, out var times)) window.Times.AddRange(times);
        _windows[budgetId] = window;
        return window;
    }

    private string PathOf(Guid budgetId) => Path.Combine(directory, budgetId + ".json");

    private IReadOnlyList<DateTimeOffset> ReadKept(Guid budgetId)
    {
        var path = PathOf(budgetId);
        if (!File.Exists(path)) return [];
        var milliseconds = JsonSerializer.Deserialize<long[]>(File.ReadAllBytes(path)) ?? [];
        return [.. milliseconds.Order().Select(DateTimeOffset.FromUnixTimeMilliseconds)];
    }
}
