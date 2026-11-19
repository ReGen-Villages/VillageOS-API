namespace vos.PlatformClient;

public static class PlatformRoute
{
    private const string Root = "/api/";

    // An address is built from the text and then normalised, so a step up the path, written plainly or
    // encoded, would leave /api/ after the check on the text had passed. Both are checked.
    public static Uri AddressOf(string myceliumUrl, string path)
    {
        var route = path.Split('?', 2)[0];
        if (!route.StartsWith(Root, StringComparison.Ordinal)
            || route.Contains('\\')
            || route.Split('/').Skip(1).Select(Uri.UnescapeDataString).Any(StepsOutOfItsPlace))
            throw NotARoute(path);

        var address = new Uri(myceliumUrl.TrimEnd('/') + path);
        return address.AbsolutePath.StartsWith(Root, StringComparison.Ordinal) ? address : throw NotARoute(path);
    }

    // For a route that takes the rest of the path as one value, so a slash in the text reaches the platform as a slash.
    public static string CatchAll(string text) => string.Join('/', text.Split('/').Select(Uri.EscapeDataString));

    // The steps .NET's Uri removes from an http address, as the address standard removes its dot segments.
    internal static bool IsRemovedFromAnAddress(string step) => step is "." or "..";

    private static bool StepsOutOfItsPlace(string segment) =>
        segment is "" || IsRemovedFromAnAddress(segment) || segment.Contains('/') || segment.Contains('\\');

    private static ArgumentException NotARoute(string path) =>
        new($"'{path}' is not a path to one of the platform's routes, which begin {Root}, take no step up the path and leave no step of it empty.", nameof(path));
}
