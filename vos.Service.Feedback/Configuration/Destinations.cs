using System.Text.Json;

namespace vos.Service.Feedback.Configuration;

// Where one application's reports are filed: the project and area, and the work-item type a bug and
// an idea each become. The types differ by process template, which is why they are named here and not
// assumed.
public sealed record Destination(string Project, string AreaPath, string BugType, string IdeaType, string[] Tags);

// Every application this deployment takes reports from, read from the file the deployment names. An
// application missing from it is refused, so a page cannot file into a project by naming it.
public sealed class Destinations
{
    private readonly IReadOnlyDictionary<string, Destination> _byApplication;

    private Destinations(IReadOnlyDictionary<string, Destination> byApplication) => _byApplication = byApplication;

    public Destination? For(string application) => _byApplication.GetValueOrDefault(application);

    private static readonly JsonSerializerOptions FileFormat = new() { PropertyNameCaseInsensitive = true };

    private sealed record DestinationInFile(string? Project, string? AreaPath, string? BugType, string? IdeaType, string[]? Tags);

    public static (Destinations? Destinations, string WhyRefused) Load(string path)
    {
        if (!File.Exists(path))
            return (null, $"The destinations file {path} does not exist.");

        Dictionary<string, DestinationInFile?>? read;
        try
        {
            read = JsonSerializer.Deserialize<Dictionary<string, DestinationInFile?>>(File.ReadAllText(path), FileFormat);
        }
        catch (JsonException error)
        {
            return (null, $"The destinations file {path} is not readable JSON: {error.Message}");
        }

        if (read is null || read.Count == 0)
            return (null, $"The destinations file {path} names no application.");

        var byApplication = new Dictionary<string, Destination>(StringComparer.OrdinalIgnoreCase);
        foreach (var (application, destination) in read)
        {
            if (destination is not { Project: { Length: > 0 } project, AreaPath: { Length: > 0 } areaPath,
                                     BugType: { Length: > 0 } bugType, IdeaType: { Length: > 0 } ideaType })
                return (null, $"The destinations file {path} gives '{application}' no project, areaPath, bugType and ideaType.");
            byApplication[application] = new Destination(project, areaPath, bugType, ideaType, destination.Tags ?? []);
        }

        return (new Destinations(byApplication), "");
    }
}
