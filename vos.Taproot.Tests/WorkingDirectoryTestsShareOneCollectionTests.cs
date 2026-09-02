using FluentAssertions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Taproot.Tests;

/// <summary>
/// The working directory is one value for the whole process, so two classes changing it in parallel
/// read each other's. The failure that costs is not the mismatch: a class restores a directory it
/// captured while another class held its own temporary one, that class deletes the temporary one, and
/// every later read of the working directory anywhere in the process throws — including in classes
/// that never touch it themselves.
/// </summary>
public class WorkingDirectoryTestsShareOneCollectionTests
{
    private static readonly string[] TouchesTheWorkingDirectory =
        ["Directory.GetCurrentDirectory", "Directory.SetCurrentDirectory", "Environment.CurrentDirectory"];

    private static readonly string SharesTheCollection =
        $"[Collection(nameof({nameof(WorkingDirectoryCollection)}))]";

    private const string ThisGuard = "WorkingDirectoryTestsShareOneCollectionTests.cs";

    [Fact]
    public void Every_class_that_touches_the_working_directory_shares_one_collection()
    {
        var sources = Sources().ToList();

        sources.Should().NotBeEmpty(
            "the scan must reach this assembly's sources, or it passes without reading anything");

        var touching = sources
            .Where(source => TouchesTheWorkingDirectory.Any(File.ReadAllText(source).Contains))
            .ToList();

        touching.Should().NotBeEmpty(
            "the literals this scans for must still be how a test reaches the working directory, "
            + "or the guard passes because it recognises nothing");

        touching
            .Where(source => !File.ReadAllText(source).Contains(SharesTheCollection))
            .Select(Path.GetFileName)
            .Should().BeEmpty(
                "a class that reads or writes the working directory outside {0} runs beside one that "
                + "changes it", nameof(WorkingDirectoryCollection));
    }

    private static IEnumerable<string> Sources() =>
        new DirectoryInfo(Path.Combine(RepositoryRoot.Find(), "vos.Taproot.Tests"))
            .EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(file => file.Name != ThisGuard)
            .Where(file => !IsBuildOutput(file.FullName))
            .Select(file => file.FullName);

    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj");
}
