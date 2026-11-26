using System.Net;

namespace vos.Service.Shared;

public enum RegistrationOutcome
{
    Accepted,
    AskAgain,
    Refused,
}

public readonly record struct RegistrationAnswer(RegistrationOutcome Outcome, TimeSpan? RetryAfter = null, string? Why = null)
{
    public static readonly RegistrationAnswer Accepted = new(RegistrationOutcome.Accepted);
    public static readonly RegistrationAnswer Refused = new(RegistrationOutcome.Refused);

    public static RegistrationAnswer AskAgainBecause(string why, TimeSpan? retryAfter = null) =>
        new(RegistrationOutcome.AskAgain, retryAfter, why);

    public static RegistrationAnswer To(HttpResponseMessage response) =>
        response.IsSuccessStatusCode ? Accepted
        : WouldBeGivenAgain(response.StatusCode) ? Refused
        : AskAgainBecause($"{(int)response.StatusCode} {response.StatusCode}", response.Headers.RetryAfter?.Delta);

    // 408 and 429 say the same request may succeed later; every other 4xx says it will not.
    private static bool WouldBeGivenAgain(HttpStatusCode status) =>
        (int)status is >= 400 and < 500 && status is not (HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests);
}
