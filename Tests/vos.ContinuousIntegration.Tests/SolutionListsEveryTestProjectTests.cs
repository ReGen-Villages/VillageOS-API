using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using vos.Tests.Shared;
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
    [Fact]
    public void Every_test_project_on_disk_is_listed_in_the_solution()
    {
        var root = RepositoryRoot.Find();
        var solution = File.ReadAllText(Path.Combine(root, RepositoryRoot.SolutionFileName));

        var missing = TestProjectFiles(new DirectoryInfo(root))
            .Select(file => Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '\\'))
            .Where(relativePath => !solution.Contains(relativePath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(missing.Count == 0,
            $"these test projects exist but are not in {RepositoryRoot.SolutionFileName}, so the build " +
            $"never runs them: {string.Join(", ", missing)}");
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
