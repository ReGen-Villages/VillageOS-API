using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace vos.ContinuousIntegration.Tests;

/// <summary>
/// The README's microservice table is the first list of what this repository runs that anyone reads,
/// and a service missing from it is invisible to a reader who has no reason to open the solution.
///
/// Nothing kept it in step with the projects on disk, so services were added over several releases
/// without a row and the table quietly became a partial list that still read as a complete one.
///
/// A service that belongs somewhere other than the table is named below with why.
/// </summary>
public class ReadmeListsEveryServiceTests
{
    private const string SolutionFileName = "VillageOS-API.sln";
    private const string ReadmeFileName = "README.md";

    private static readonly Dictionary<string, string> ListedElsewhere = new()
    {
        ["vos.Service.Shared"] = "the library every service builds on, not a service anyone runs",
        ["vos.Service.Shared.Contracts"] = "the request and response schemas, not a service anyone runs",
    };

    [Fact]
    public void Every_service_project_has_a_row_in_the_readme_table()
    {
        var root = RepositoryRoot();
        var readme = File.ReadAllText(Path.Combine(root, ReadmeFileName));

        var unlisted = ServiceProjectNames(root)
            .Where(service => !ListedElsewhere.ContainsKey(service))
            .Where(service => !readme.Contains($"| {service} |", StringComparison.Ordinal))
            .ToList();

        Assert.True(unlisted.Count == 0,
            $"these services have no row in the {ReadmeFileName} microservice table, so a reader of the "
            + $"front page cannot see they exist: {string.Join(", ", unlisted)}. Add a row, or name the "
            + $"project in {nameof(ListedElsewhere)} with why it does not belong in the table.");
    }

    /// <summary>Every service directory, whatever language it is written in — the echo services in Go,
    /// Node, Python and Rust are rows in the same table.</summary>
    private static IEnumerable<string> ServiceProjectNames(string root) =>
        new DirectoryInfo(root)
            .EnumerateDirectories("vos.Service.*")
            .Select(service => service.Name);

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
}
