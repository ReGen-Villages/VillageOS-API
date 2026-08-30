using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using vos.Tests.Shared;
using Xunit;

namespace vos.ContinuousIntegration.Tests;

/// <summary>
/// A service entry point holds nothing but wiring, and the decisions it used to make live in
/// vos.Service.Shared where they are covered. Such a file is excluded from the coverage figure, because
/// counted it reads as a hundred-odd untested lines and pulls its whole service down — which is loud
/// enough to be ignored and drowns the gaps worth looking at.
///
/// Nothing but this made a new service's entry point join that list, so some were simply never added
/// and read as the least covered code in the repository while holding no logic at all.
///
/// A service whose entry point is genuinely counted is named below with why. A new one is in neither
/// list and fails here until somebody decides which it is.
/// </summary>
public class ServiceEntryPointsAreDecidedTests
{
    private const string RunSettingsFileName = "coverage.runsettings";

    private static readonly Dictionary<string, string> CountedOnPurpose = new()
    {
        ["vos.Service.Forage"] = "its endpoints are driven end to end through WebApplicationFactory",
        ["vos.Service.Intake"] = "its endpoints are driven end to end through WebApplicationFactory",
        ["vos.Service.Phloem"] = "its entry point still holds request handling with no tests behind it",
        ["vos.Service.Xylem"] = "its entry point still holds request handling with no tests behind it",
    };

    [Fact]
    public void Every_service_entry_point_is_either_excluded_from_coverage_or_counted_for_a_stated_reason()
    {
        var root = RepositoryRoot.Find();
        var excluded = ExcludedFiles(root);

        var undecided = ServiceDirectories(root)
            .Where(service => !excluded.Contains($"**/{service}/Program.cs"))
            .Where(service => !CountedOnPurpose.ContainsKey(service))
            .ToList();

        Assert.True(undecided.Count == 0,
            $"these service entry points are counted in the coverage figure and named nowhere as to why: "
            + $"{string.Join(", ", undecided)}. Either exclude the file in {RunSettingsFileName}, or name "
            + $"the service in {nameof(CountedOnPurpose)} with the reason it is measured.");
    }

    [Fact]
    public void Nothing_is_both_excluded_from_coverage_and_named_as_counted()
    {
        var excluded = ExcludedFiles(RepositoryRoot.Find());

        var contradictory = CountedOnPurpose.Keys
            .Where(service => excluded.Contains($"**/{service}/Program.cs"))
            .ToList();

        Assert.True(contradictory.Count == 0,
            $"these services are excluded in {RunSettingsFileName} and also named in "
            + $"{nameof(CountedOnPurpose)}, so the stated reason describes a file nobody measures: "
            + $"{string.Join(", ", contradictory)}");
    }

    private static HashSet<string> ExcludedFiles(string root)
    {
        var runSettings = XDocument.Load(Path.Combine(root, RunSettingsFileName));
        var excludeByFile = runSettings.Descendants("ExcludeByFile").Single().Value;

        return excludeByFile
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ServiceDirectories(string root) =>
        ServiceEntryPoints.Services(root).Select(service => service.Name);
}
