using System.Text.Json;
using System.Text.Json.Serialization;
using vos.Service.Intake.Models;

namespace vos.Service.Intake.Services;

public static class SubmissionReader
{
    // A field this service does not write is refused rather than ignored. Silently dropping part of a
    // submission would leave the planner believing it was recorded.
    private static readonly JsonSerializerOptions SubmissionFormat = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static Submission Read(string document)
    {
        try
        {
            return JsonSerializer.Deserialize<Submission>(document, SubmissionFormat)
                ?? throw new SubmissionError("The submission is empty.");
        }
        catch (JsonException error)
        {
            throw new SubmissionError($"The submission could not be read: {error.Message}");
        }
    }
}
