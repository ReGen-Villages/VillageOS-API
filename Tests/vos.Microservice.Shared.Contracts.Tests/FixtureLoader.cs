namespace vos.Microservice.Shared.Contracts.Tests;

/// <summary>
/// Locates fixture JSON files copied to the test output directory under
/// <c>Fixtures/&lt;schema-folder&gt;/&lt;name&gt;.json</c>.
/// </summary>
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
