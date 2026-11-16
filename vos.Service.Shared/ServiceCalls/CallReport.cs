namespace vos.Service.Shared.ServiceCalls;

// A failure is made only with its reason, because the broker refuses one without.
public sealed record CallReport
{
    public static CallReport Received { get; } = new("received", null);

    public static CallReport Completed { get; } = new("completed", null);

    public static CallReport Failed(string reason) =>
        string.IsNullOrWhiteSpace(reason)
            ? throw new ArgumentException("A failed call is reported with the reason it failed.", nameof(reason))
            : new CallReport("failed", reason);

    private CallReport(string word, string? reason)
    {
        Word = word;
        Reason = reason;
    }

    public string Word { get; }

    public string? Reason { get; }

    public bool FinishesTheCall => this != Received;
}
