using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using vos.Tests.Shared;
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
    private const string ReadmeFileName = "README.md";

    private static readonly Dictionary<string, string> ListedElsewhere = new()
    {
        ["vos.Service.Shared"] = "the library every service builds on, not a service anyone runs",
        ["vos.Service.Shared.Contracts"] = "the request and response schemas, not a service anyone runs",
    };

    [Fact]
    public void Every_service_project_has_a_row_in_the_readme_table()
    {
        var root = RepositoryRoot.Find();
        var readme = File.ReadAllText(Path.Combine(root, ReadmeFileName));

        var unlisted = ServiceProjects.NamesUnder(root)
            .Where(service => !ListedElsewhere.ContainsKey(service))
            .Where(service => !readme.Contains($"| {service} |", StringComparison.Ordinal))
            .ToList();

        Assert.True(unlisted.Count == 0,
            $"these services have no row in the {ReadmeFileName} microservice table, so a reader of the "
            + $"front page cannot see they exist: {string.Join(", ", unlisted)}. Add a row, or name the "
            + $"project in {nameof(ListedElsewhere)} with why it does not belong in the table.");
    }
}
