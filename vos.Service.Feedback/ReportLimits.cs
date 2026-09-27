namespace vos.Service.Feedback;

// A report is read into memory whole, so the body is capped before anything looks at it. The cap
// leaves room for a full-screen picture the panel has already shrunk and compressed.
public static class ReportLimits
{
    public const int MaximumBodyBytes = 8 * 1024 * 1024;

    // What Azure DevOps takes in a title.
    public const int TitleCharacters = 255;

    public const int DescriptionCharacters = 20_000;

    public const int ContextCharacters = 2_000;
}

// Generous for people reporting what they see, tight for a script filing into the board. Counted per
// address, before the caller is known, so a flood never reaches the platform or DevOps.
public static class ReportRate
{
    public const string PolicyName = "reports";
    public const int RequestsAllowed = 30;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
}
