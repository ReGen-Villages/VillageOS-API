namespace vos.Taproot;

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
            || route.Split('/').Select(Uri.UnescapeDataString).Any(StepsOutOfItsPlace))
            throw NotARoute(path);

        var address = new Uri(myceliumUrl.TrimEnd('/') + path);
        return address.AbsolutePath.StartsWith(Root, StringComparison.Ordinal) ? address : throw NotARoute(path);
    }

    private static bool StepsOutOfItsPlace(string segment) =>
        segment is "." or ".." || segment.Contains('/') || segment.Contains('\\');

    private static ArgumentException NotARoute(string path) =>
        new($"'{path}' is not a path to one of the platform's routes, which begin {Root} and take no step up the path.", nameof(path));
}
