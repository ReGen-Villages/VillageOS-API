using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using vos.Tests.Shared;
using Xunit;

namespace vos.ContinuousIntegration.Tests;

/// <summary>
/// A worktree links the main checkout's node_modules rather than installing a second copy of it. Git
/// records a link as a file whatever it points at, and an ignore rule ending in a slash matches
/// directories only, so a rule that reads as covering node_modules leaves every worktree's link
/// untracked and staging the whole tree commits it.
///
/// <para>The main checkout holds a real directory there, which such a rule does match, so nothing shows
/// wrong in the place most of the work happens. Asked of git rather than read out of the file, because
/// the pattern is not what is wrong with it: the rule reads correctly and does not match.</para>
/// </summary>
public class NodeModulesIsIgnoredWhereverItIsLinkedTests
{
    [Fact]
    public void Every_npm_project_has_its_node_modules_ignored_as_a_link()
    {
        var root = RepositoryRoot.Find();

        var exposed = NpmProjectDirectories(new DirectoryInfo(root))
            .Select(directory => Path.GetRelativePath(root, Path.Combine(directory, "node_modules")))
            .Where(link => !IsIgnored(link))
            .ToList();

        Assert.True(exposed.Count == 0,
            "a worktree links node_modules rather than installing it, and git records a link as a file, "
            + "so an ignore rule ending in a slash does not match one. The rule covering these has to "
            + $"lose its trailing slash: {string.Join("; ", exposed)}");
    }

    /// <summary>
    /// Hidden directories are skipped because a git worktree lives under one and holds a second
    /// checkout of every project this would otherwise find.
    /// </summary>
    private static IEnumerable<string> NpmProjectDirectories(DirectoryInfo directory)
    {
        if (directory.EnumerateFiles("package.json").Any()) yield return directory.FullName;

        var searchable = directory.EnumerateDirectories()
            .Where(child => !child.Name.StartsWith('.')
                            && child.Name is not ("bin" or "obj" or "node_modules"));
        foreach (var child in searchable)
        foreach (var found in NpmProjectDirectories(child))
            yield return found;
    }

    private static bool IsIgnored(string path)
    {
        var invocation = new ProcessStartInfo("git")
        {
            WorkingDirectory = RepositoryRoot.Find(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "check-ignore", "-q", "--no-index", path })
            invocation.ArgumentList.Add(argument);

        using var process = Process.Start(invocation)!;
        process.WaitForExit();
        return process.ExitCode == 0;
    }
}
