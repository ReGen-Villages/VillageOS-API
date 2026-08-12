using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace vos.ContinuousIntegration.Tests;

/// <summary>
/// The build runs the tests by handing the runner the solution, so the solution decides what runs. A
/// test project that nobody added to it is still built, still passes locally under its own project,
/// and is simply never run on the agent — and a suite that does not run leaves no mark on a green
/// build. Before, the build matched test projects by wildcard and a new one was picked up by having
/// been written; this is what replaces that.
/// </summary>
public class SolutionListsEveryTestProjectTests
{
    private const string SolutionFileName = "VillageOS-API.sln";

    [Fact]
    public void Every_test_project_on_disk_is_listed_in_the_solution()
    {
        var root = RepositoryRoot();
        var solution = File.ReadAllText(Path.Combine(root, SolutionFileName));

        var missing = TestProjectFiles(new DirectoryInfo(root))
            .Select(file => Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '\\'))
            .Where(relativePath => !solution.Contains(relativePath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(missing.Count == 0,
            $"these test projects exist but are not in {SolutionFileName}, so the build never runs " +
            $"them: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// Walked up to rather than a path relative to the test output directory: how deep that directory
    /// sits below the repository root differs between a run from the solution, from a worktree and
    /// from the build agent.
    /// </summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException($"{SolutionFileName} was not found above the test output directory.");
    }

    /// <summary>
    /// Hidden directories are skipped because a git worktree lives under one and holds a second
    /// checkout of every project this would otherwise find.
    /// </summary>
    private static IEnumerable<string> TestProjectFiles(DirectoryInfo directory)
    {
        foreach (var file in directory.EnumerateFiles("*Tests.csproj")) yield return file.FullName;

        var searchable = directory.EnumerateDirectories()
            .Where(child => !child.Name.StartsWith('.')
                            && child.Name is not ("bin" or "obj" or "node_modules"));

        foreach (var child in searchable)
        foreach (var file in TestProjectFiles(child))
            yield return file;
    }
}
