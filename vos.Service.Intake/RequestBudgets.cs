namespace vos.Service.Intake;

public sealed record RequestBudget(int RequestCount, TimeSpan Window)
{
    public static readonly RequestBudget Fallback = new(20, TimeSpan.FromSeconds(600));
}

public sealed record DeclaredRequestBudgets(RequestBudget Verified, RequestBudget Unverified, IReadOnlyList<string> FallbackReasons)
{
    public static readonly DeclaredRequestBudgets Fallback = new(RequestBudget.Fallback, RequestBudget.Fallback, []);
}

public sealed class RequestBudgets
{
    private DeclaredRequestBudgets _current = DeclaredRequestBudgets.Fallback;

    public DeclaredRequestBudgets Current => Volatile.Read(ref _current);

    public void Replace(DeclaredRequestBudgets budgets) => Volatile.Write(ref _current, budgets);
}

public static class RequestPartition
{
    public static (string Key, RequestBudget Budget) For(
        string? presentedTicket, string? sourceAddress, SubmissionTicket tickets, DeclaredRequestBudgets budgets) =>
        (sourceAddress ?? "unknown", RequestBudget.Fallback);
}
