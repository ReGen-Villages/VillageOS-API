using System.Net;
using System.Text.Json;

namespace vos.Service.Shared;

public enum PlatformOutcome
{
    Done,
    NotFound,
    Refused,
}

// Carries the status and body as they came, because each service decides what a refusal means for its own caller.
public sealed record PlatformAnswer(PlatformOutcome Outcome, HttpStatusCode Status, string Body)
{
    public bool IsDone => Outcome == PlatformOutcome.Done;

    public JsonElement Json()
    {
        using var document = JsonDocument.Parse(Body);
        return document.RootElement.Clone();
    }

    public string? ErrorWords() => ErrorWordsIn(Body);

    public static string? ErrorWordsIn(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return MyceliumClientBase.TryGetPropertyCaseInsensitive(document.RootElement, "error", out var error)
                   && error.ValueKind == JsonValueKind.String
                ? error.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
