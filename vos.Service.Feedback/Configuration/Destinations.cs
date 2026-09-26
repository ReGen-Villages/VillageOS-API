using Microsoft.Extensions.Configuration;

namespace vos.Service.Feedback.Configuration;

// Where one application's reports are filed: the project and area, and the work-item type a bug and
// an idea each become. The types differ by process template, which is why they are named here and not
// assumed.
public sealed record Destination(string Project, string AreaPath, string BugType, string IdeaType, string[] Tags);

// Every application this deployment takes reports from, read from the Destinations section of its
// settings. An application missing from it is refused, so a page cannot file into a project by naming it.
public sealed class Destinations
{
    private const string Section = "Destinations";

    private readonly IReadOnlyDictionary<string, Destination> _byApplication;

    private Destinations(IReadOnlyDictionary<string, Destination> byApplication) => _byApplication = byApplication;

    public Destination? For(string application) => _byApplication.GetValueOrDefault(application);

    public static (Destinations? Destinations, string WhyRefused) From(IConfiguration? configuration)
    {
        var applications = configuration?.GetSection(Section).GetChildren().ToList() ?? [];
        if (applications.Count == 0)
            return (null, $"The {Section} section of the settings names no application.");

        var byApplication = new Dictionary<string, Destination>(StringComparer.OrdinalIgnoreCase);
        foreach (var application in applications)
        {
            if (application["Project"] is not { Length: > 0 } project || application["AreaPath"] is not { Length: > 0 } areaPath
                || application["BugType"] is not { Length: > 0 } bugType || application["IdeaType"] is not { Length: > 0 } ideaType)
                return (null, $"{Section}:{application.Key} needs a Project, AreaPath, BugType and IdeaType.");

            var tags = application.GetSection("Tags").GetChildren()
                .Select(tag => tag.Value).OfType<string>().Where(tag => tag.Length > 0).ToArray();
            byApplication[application.Key] = new Destination(project, areaPath, bugType, ideaType, tags);
        }

        return (new Destinations(byApplication), "");
    }
}
