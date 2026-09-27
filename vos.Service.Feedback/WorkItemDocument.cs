using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using vos.Service.Feedback.Configuration;

namespace vos.Service.Feedback;

// Who a report is filed under. Through names the service that vouched for them, when one did.
public sealed record Reporter(string Name, string? Role, string? Through);

// Everything the reporter wrote is encoded before it goes into the HTML body, so what they typed is
// shown and never runs.
public static class WorkItemDocument
{
    private const string ReportedInAppTag = "Reported in app";

    public static JsonArray For(
        ValidReport report, Reporter reporter, string? modelName, Destination destination, string? attachmentUrl,
        DateTimeOffset reportedAt)
    {
        var bodyField = report.Kind == ReportKind.Bug ? "Microsoft.VSTS.TCM.ReproSteps" : "System.Description";
        var tags = new[] { ReportedInAppTag, report.Application }.Concat(destination.Tags);

        var patch = new JsonArray
        {
            Add("/fields/System.Title", report.Title),
            Add("/fields/System.AreaPath", destination.AreaPath),
            Add("/fields/System.Tags", string.Join("; ", tags)),
            Add($"/fields/{bodyField}", Body(report, reporter, modelName, attachmentUrl, reportedAt)),
        };
        if (attachmentUrl is not null)
            patch.Add(Add("/relations/-", new JsonObject
            {
                ["rel"] = "AttachedFile",
                ["url"] = attachmentUrl,
                ["attributes"] = new JsonObject { ["comment"] = "Screenshot sent with the report" },
            }));
        return patch;
    }

    private static JsonObject Add(string path, JsonNode value) => new() { ["op"] = "add", ["path"] = path, ["value"] = value };

    private static string Body(ValidReport report, Reporter reporter, string? modelName, string? attachmentUrl, DateTimeOffset reportedAt)
    {
        var html = new StringBuilder();
        html.Append(report.Description.Length > 0
            ? $"<p>{string.Join("<br>", report.Description.ReplaceLineEndings("\n").Split('\n').Select(Encoded))}</p>"
            : "<p><i>No description given.</i></p>");
        if (attachmentUrl is not null)
            html.Append($"<p><img src=\"{Encoded(attachmentUrl)}\" alt=\"Screenshot sent with the report\"></p>");

        html.Append("<h3>Where it was reported</h3><table>");
        Row(html, "Application", Encoded(report.Application));
        Row(html, "Page", PageLink(report.Context.PageAddress));
        Row(html, "Reported by", Encoded(ReporterText(reporter)));
        Row(html, "Model", Encoded(modelName));
        Row(html, "Browser", Encoded(report.Context.Browser));
        Row(html, "Screen", Encoded(report.Context.ScreenSize));
        Row(html, "Language", Encoded(report.Context.Language));
        Row(html, "Time", Encoded(reportedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'")));
        html.Append("</table>");
        return html.ToString();
    }

    private static string ReporterText(Reporter reporter) =>
        reporter.Through is not null ? $"{reporter.Name}, through {reporter.Through}"
        : reporter.Role is not null ? $"{reporter.Name} ({reporter.Role})"
        : reporter.Name;

    // A link only for a web address, so a page address cannot become a script to click.
    private static string PageLink(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var page) && (page.Scheme == Uri.UriSchemeHttps || page.Scheme == Uri.UriSchemeHttp)
            ? $"<a href=\"{Encoded(address)}\">{Encoded(address)}</a>"
            : Encoded(address);

    private static void Row(StringBuilder html, string label, string value)
    {
        if (value.Length > 0)
            html.Append($"<tr><td><b>{label}</b></td><td>{value}</td></tr>");
    }

    // Every script left readable, so a reporter's own language reads as written rather than as numbered
    // entities; the characters that make markup are encoded whatever the range.
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(System.Text.Unicode.UnicodeRanges.All);

    private static string Encoded(string? text) => Encoder.Encode(text ?? "");
}
