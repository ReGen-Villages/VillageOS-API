using FluentAssertions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Taproot.Tests;

/// <summary>
/// The working directory is one value for the whole process, so two classes changing it in parallel
/// each read the value the other set. That mismatch is not the failure that costs. A class captures
/// the directory it will restore while another class is holding its own temporary one; that class
/// deletes the temporary one; the restore then points the process at a directory that is gone, and
/// every later read of the working directory throws — including in classes that never touch it.
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
        var touching = Sources()
            .Select(source => (Name: Path.GetFileName(source), Text: File.ReadAllText(source)))
            .Where(source => TouchesTheWorkingDirectory.Any(source.Text.Contains))
            .ToList();

        touching.Should().NotBeEmpty(
            "the literals this scans for must still be how a test reaches the working directory, "
            + "or the guard passes because it recognises nothing");

        touching
            .Where(source => !source.Text.Contains(SharesTheCollection))
            .Select(source => source.Name)
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
