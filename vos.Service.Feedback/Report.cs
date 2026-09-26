using System.Text.RegularExpressions;

namespace vos.Service.Feedback;

public sealed record ReportContext(string? PageAddress, string? Browser, string? ScreenSize, string? Language);

// What the panel posts. Reporter is read only from a service passing a report on for somebody it has
// checked; a person's report is filed under the name their own token carries.
public sealed record ReportRequest(
    string? Application, string? Kind, string? Title, string? Description, string? Screenshot, string? Reporter,
    ReportContext? Context);

public enum ReportKind { Bug, Idea }

public sealed record Screenshot(byte[] Bytes, string MediaType)
{
    public string FileName => MediaType switch
    {
        "image/jpeg" => "screenshot.jpg",
        "image/webp" => "screenshot.webp",
        _ => "screenshot.png",
    };
}

public sealed record ValidReport(
    string Application, ReportKind Kind, string Title, string Description, Screenshot? Screenshot,
    string? Reporter, ReportContext Context);

public static partial class ReportReading
{
    private static readonly string[] PictureTypes = ["image/png", "image/jpeg", "image/webp"];

    [GeneratedRegex(@"^data:(?<type>[a-z]+/[a-z0-9.+-]+);base64,(?<data>[A-Za-z0-9+/=]+)$")]
    private static partial Regex DataAddress();

    public static (ValidReport? Report, Refusal? Refusal) Read(ReportRequest? request)
    {
        if (request is null)
            return (null, new Refusal(RefusalCode.ReportUnreadable, "The report could not be read."));

        if (string.IsNullOrWhiteSpace(request.Application))
            return Missing("application");

        ReportKind kind;
        switch (request.Kind)
        {
            case "bug": kind = ReportKind.Bug; break;
            case "idea": kind = ReportKind.Idea; break;
            default: return (null, new Refusal(RefusalCode.KindUnknown, "A report is a bug or an idea."));
        }

        var title = request.Title?.Trim() ?? "";
        if (title.Length == 0)
            return Missing("title");
        if (title.Length > ReportLimits.TitleCharacters)
            return TooLong("title", ReportLimits.TitleCharacters);

        var description = request.Description?.Trim() ?? "";
        if (description.Length > ReportLimits.DescriptionCharacters)
            return TooLong("description", ReportLimits.DescriptionCharacters);

        var context = request.Context ?? new ReportContext(null, null, null, null);
        if (new[] { context.PageAddress, context.Browser, context.ScreenSize, context.Language }
            .Any(value => value?.Length > ReportLimits.ContextCharacters))
            return TooLong("context", ReportLimits.ContextCharacters);

        Screenshot? screenshot = null;
        if (!string.IsNullOrEmpty(request.Screenshot))
        {
            screenshot = PictureIn(request.Screenshot);
            if (screenshot is null)
                return (null, new Refusal(RefusalCode.ScreenshotUnreadable, "The screenshot is not a PNG, JPEG or WebP picture."));
        }

        return (new ValidReport(request.Application.Trim(), kind, title, description, screenshot,
            string.IsNullOrWhiteSpace(request.Reporter) ? null : request.Reporter.Trim(), context), null);
    }

    private static Screenshot? PictureIn(string dataAddress)
    {
        var match = DataAddress().Match(dataAddress);
        if (!match.Success || !PictureTypes.Contains(match.Groups["type"].Value))
            return null;

        try
        {
            return new Screenshot(Convert.FromBase64String(match.Groups["data"].Value), match.Groups["type"].Value);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static (ValidReport?, Refusal?) Missing(string field) =>
        (null, new Refusal(RefusalCode.FieldMissing, $"The report has no {field}.",
            new Dictionary<string, object?> { ["field"] = field }));

    private static (ValidReport?, Refusal?) TooLong(string field, int characters) =>
        (null, new Refusal(RefusalCode.FieldTooLong, $"The {field} may be at most {characters} characters.",
            new Dictionary<string, object?> { ["field"] = field, ["characters"] = characters }));
}
