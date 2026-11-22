using System.Text.Json;
using System.Text.RegularExpressions;

namespace vos.Hypha.Tools.Pages;

// A path comes out of a page description, so it is taken only when it matches one of these shapes character for character; otherwise a tool marked as only reading could post to any route a description named.
public static partial class PageReadRoutes
{
    private const string Identifier = "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}";

    // One segment of a path: letters, digits and the marks a name is written with, or a percent-encoded
    // character other than a slash, a backslash or a dot, which could turn one segment into two or step out of it.
    private const string Segment = @"(?:[A-Za-z0-9_~!*'()-]|\.(?!\.)|%(?!2[EeFf]|5[Cc])[0-9A-Fa-f]{2})+";

    private const string Query = @"(?:\?[A-Za-z0-9_~.,+=&%*-]*)?";

    public static string? RefusalToGet(string path) =>
        ThingsInAState().IsMatch(path) || RangesOfAThing().IsMatch(path) ? null : Refusal("fetch", path);

    public static string? RefusalToPost(string path) =>
        TemporalRead().IsMatch(path) || ServiceEndpoint().IsMatch(path) ? null : Refusal("post to", path);

    public static string? RefusalToPress(string path) =>
        ServiceEndpoint().IsMatch(path) || PressablePlatformRoutes.Contains(path)
            ? null
            : $"A press is not posted to {JsonSerializer.Serialize(path)}. It is posted to a service under /api/endpoints/, "
              + $"or to one of these platform routes: {string.Join(", ", PressablePlatformRoutes)}.";

    private static readonly IReadOnlyList<string> PressablePlatformRoutes = ["/api/model/promote"];

    private static string Refusal(string verb, string path) =>
        $"A page read does not {verb} \"{path}\". It reads the Things in a state, a Thing's ranges, the two temporal reads, "
        + "and a service under /api/endpoints/, each as a path on this Mycelium.";

    [GeneratedRegex(@"\A/api/states/" + Segment + "/things" + Query + @"\z")]
    private static partial Regex ThingsInAState();

    [GeneratedRegex(@"\A/api/things/" + Identifier + @"/ranges\z")]
    private static partial Regex RangesOfAThing();

    [GeneratedRegex(@"\A/api/temporal/(?:aggregate|reduce)\z")]
    private static partial Regex TemporalRead();

    [GeneratedRegex(@"\A/api/endpoints/" + Segment + @"\z")]
    private static partial Regex ServiceEndpoint();
}
