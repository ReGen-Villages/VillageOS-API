namespace vos.Tests.Shared;

public static class TestSeeds
{
    public static string Folder => Path.Combine(RepositoryRoot.Find(), "Tests", "vos.Tests.Shared", "Fixtures");

    public static IReadOnlyList<string> All => [.. Directory.EnumerateFiles(Folder, "*.seed.json").Order()];

    public static string Catchments => Path.Combine(Folder, "Catchments.seed.json");

    public static string CatchmentsWithAnUnreadablePage =>
        Path.Combine(Folder, "CatchmentsWithAnUnreadablePage.seed.json");
}
