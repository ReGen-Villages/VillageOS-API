using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace vos.ContinuousIntegration.Tests;

/// <summary>
/// Trellis, Taproot and the services ship together and say one version (#6467).
///
/// Trellis is a JavaScript application and the rest are .NET projects, so the same version is written
/// in two files in two formats. Nothing but a check keeps two files in step, and a client reporting a
/// version the platform does not is a pair that cannot be spoken about as one release.
///
/// This asserts the repository agrees with itself. That it also agrees with the platform is checked
/// where both are checked out, which is the platform's own build: this repository has no platform to
/// ask, and must not have one.
/// </summary>
public class ShippedComponentsDeclareOneVersionTests
{
    private const string SolutionFileName = "VillageOS-API.sln";

    [Fact]
    public void Trellis_declares_the_version_the_dotnet_projects_declare()
    {
        Assert.Equal(DeclaredByDotNetProjects(), DeclaredByTrellis());
    }

    /// <summary>Pre-release is the point of the number, not an accident of it: the suffix is what says
    /// the platform makes no compatibility promise, and a release version would say the opposite by
    /// saying nothing.</summary>
    [Fact]
    public void The_version_says_it_is_pre_release()
    {
        Assert.Contains("-", DeclaredByDotNetProjects());
    }

    private static string DeclaredByDotNetProjects()
    {
        var props = Path.Combine(RepositoryRoot(), "Directory.Build.props");
        var declared = Regex.Match(File.ReadAllText(props), @"<Version>([^<]+)</Version>");
        Assert.True(declared.Success,
            $"{props} declares no <Version>, so Taproot and every service inherit none.");
        return declared.Groups[1].Value.Trim();
    }

    private static string DeclaredByTrellis()
    {
        var package = Path.Combine(RepositoryRoot(), "vos.Trellis", "package.json");
        using var document = JsonDocument.Parse(File.ReadAllText(package));
        Assert.True(document.RootElement.TryGetProperty("version", out var version),
            $"{package} declares no version.");
        return version.GetString()!;
    }

    /// <summary>Walked up to rather than a path relative to the test output directory: how deep that
    /// directory sits below the repository root differs between a run from the solution, from a
    /// worktree and from the build agent.</summary>
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
