namespace vos.ManagedMicroservice.Shared.Contracts.Tests;

// Locates fixture JSON files copied to the test output directory under
// Fixtures/<schema-folder>/<name>.json.
internal static class FixtureLoader
{
    public static string Read(string schemaFolder, string fixtureName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", schemaFolder, fixtureName + ".json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Fixture not found: {path}", path);
        return File.ReadAllText(path);
    }

    public static IEnumerable<string> EnumerateSchemaFolders()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        if (!Directory.Exists(root))
            yield break;
        foreach (var dir in Directory.EnumerateDirectories(root))
            yield return Path.GetFileName(dir);
    }
}
