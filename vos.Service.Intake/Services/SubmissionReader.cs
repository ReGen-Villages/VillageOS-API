using System.Text.Json;
using System.Text.Json.Serialization;
using vos.Service.Intake.Models;

namespace vos.Service.Intake.Services;

/// <summary>A posted document read into a submission and held to what a submission may contain.</summary>
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
        Submission submission;
        try
        {
            submission = JsonSerializer.Deserialize<Submission>(document, SubmissionFormat)
                ?? throw new SubmissionError("The submission is empty.");
        }
        catch (JsonException error)
        {
            throw new SubmissionError($"The submission could not be read: {error.Message}");
        }

        SubmissionLimits.Enforce(submission);
        return submission;
    }
}
