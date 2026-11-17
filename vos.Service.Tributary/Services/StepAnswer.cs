using System.Text.Json;

namespace vos.Service.Tributary.Services;

// What one call came to, as Tributary's step answers it. The outcome holds the call exactly as it was handed, so
// whatever reads the answer can match it to the call it planned, and either what the call wrote or why it failed.
public sealed record StepAnswer(
    bool Succeeded,
    IReadOnlyDictionary<string, object?> Outcome,
    string? Reason,
    bool CanBeTriedAgain,
    TimeSpan? Wait)
{
    public const string CallField = "call";
    public const string WrittenField = "written";
    public const string ReasonField = "reason";

    // A reason is read by a person deciding whether a gap matters, so it carries the provider's words but not a
    // whole error document.
    public const int LongestWords = 300;

    public static StepAnswer From(EndpointCallResult result, JsonElement call, DateTimeOffset now)
    {
        if (result.Ingest is { } ingest)
            return Success(call, ingest.Written);

        // Tributary answers a ProblemError when a read, a write or the call itself did not go through, which a later attempt may get past.
        if (result.Error is { } error)
            return Failure(call, $"{error.StatusCode}: {error.Message}", canBeTriedAgain: error is ProblemError, wait: null);

        if (result.StatusCode is >= 200 and < 300)
            return Success(call, written: null);

        return Failure(call, $"{result.StatusCode}: {Shortened(result.Content ?? string.Empty)}",
            CanChange(result), WaitAsked(result, now));
    }

    private static bool CanChange(EndpointCallResult refused) =>
        refused.ProviderAway || refused.StatusCode is 408 or 425 or 429 or >= 500;

    private static TimeSpan? WaitAsked(EndpointCallResult refused, DateTimeOffset now) => refused.RetryAfter switch
    {
        { Delta: { } delta } => delta,
        { Date: { } until } => until > now ? until - now : TimeSpan.Zero,
        _ => null,
    };

    private static StepAnswer Success(JsonElement call, IReadOnlyDictionary<string, object?>? written) =>
        new(true, new Dictionary<string, object?> { [CallField] = call, [WrittenField] = written }, null, false, null);

    private static StepAnswer Failure(JsonElement call, string reason, bool canBeTriedAgain, TimeSpan? wait) =>
        new(false, new Dictionary<string, object?> { [CallField] = call, [ReasonField] = reason }, reason, canBeTriedAgain, wait);

    private static string Shortened(string words)
    {
        var trimmed = words.Trim();
        return trimmed.Length <= LongestWords ? trimmed : trimmed[..LongestWords] + "…";
    }
}
