namespace vos.Service.Feedback;

// A refusal as the report panel reads it: a code it words in the reader's language, the values those
// words need, and English for a panel that does not know the code.
public sealed record Refusal(string Code, string Error, IReadOnlyDictionary<string, object?>? Values = null);

public static class RefusalCode
{
    public const string SignInRequired = "signInRequired";
    public const string TooManyRequests = "tooManyRequests";
    public const string ServiceUnavailable = "serviceUnavailable";
    public const string FilingFailed = "filingFailed";
    public const string ReportTooLarge = "reportTooLarge";
    public const string ReportUnreadable = "reportUnreadable";
    public const string ApplicationUnknown = "applicationUnknown";
    public const string KindUnknown = "kindUnknown";
    public const string FieldMissing = "fieldMissing";
    public const string FieldTooLong = "fieldTooLong";
    public const string ScreenshotUnreadable = "screenshotUnreadable";
    public const string ReporterMissing = "reporterMissing";
}
