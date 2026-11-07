using Microsoft.Extensions.Configuration;

namespace vos.Service.Tributary.Services;

// A 404 is a provider's answer, except when the proxy in front of the provider answers it to every
// request while the provider restarts. The two are told apart by the words: the proxy's are fixed and
// name no resource, so a 404 carrying exactly them is the provider being away, not an answer about the
// subject. Such a call is asked once more after the wait, and relayed as a refusal if the words come again.
public sealed record ProviderUnavailability(string? Answer, TimeSpan RetryAfter)
{
    public const string ConfigurationSection = "ProviderUnavailable";
    public const string AnswerProperty = "unavailableAnswer";
    public const string RetryAfterProperty = "unavailableRetryAfter";

    public static ProviderUnavailability FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(ConfigurationSection);
        var seconds = section.GetValue<double?>("RetryAfterSeconds") ?? 0;
        return new ProviderUnavailability(section["Answer"], TimeSpan.FromSeconds(Math.Max(0, seconds)));
    }

    // The words are read only for a 404, so a binary body is not decoded for an ordinary answer.
    public bool IsAnswered(int status, Func<string> words) =>
        status == 404
        && !string.IsNullOrWhiteSpace(Answer)
        && string.Equals(words().Trim(), Answer.Trim(), StringComparison.Ordinal);
}
