namespace vos.Service.Intake;

/// <summary>What one source may ask of the public route before it is made to wait.</summary>
/// <remarks>A submission is one request, and the ticket the form asks for first is another, so the budget
/// is far above what filling a form in costs and far below what a submitter flooding the staging model
/// would need. Sources sit behind whatever address the reverse proxy forwards, which is a shared one for
/// everybody behind a single connection — hence a budget that a household or an office cannot reach by
/// submitting honestly.</remarks>
public static class SubmissionRate
{
    public const string PolicyName = "public-submission";

    public const int RequestsAllowed = 20;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
}
