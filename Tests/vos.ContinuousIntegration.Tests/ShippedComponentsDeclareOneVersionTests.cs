using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace vos.ContinuousIntegration.Tests;

/// <summary>
/// Trellis, Taproot and the services ship together and say one version (#6467).
///
/// Trellis is a JavaScript application and the rest are .NET projects, so the same version is written
/// in two formats. Nothing but a check keeps two files in step, and a client reporting a version the
/// platform does not is a pair that cannot be spoken about as one release.
///
/// The default is that a component must match. Anything exempt is named below with a reason, so a
/// component added later fails until somebody decides which it is rather than passing by being
/// unnoticed.
///
/// This asserts the repository agrees with itself. That it also agrees with the platform is checked
/// where both are checked out, which is the platform's own build: there is no platform here to ask,
/// and there must not be.
/// </summary>
public class ShippedComponentsDeclareOneVersionTests
{
    private const string SolutionFileName = "VillageOS-API.sln";

    /// <summary>Not shipped with the platform, so not held to its version.
    ///
    /// The three under tools/ build documentation and mirror a wiki; they run on somebody's machine
    /// and are never handed to anyone. The echo package is one half of a worked example showing what
    /// a microservice in another language has to implement, and its version is part of the example.
    /// </summary>
    private static readonly string[] NotShipped =
    [
        Path.Combine("tools", "docs-pdf"),
        Path.Combine("tools", "wiki-mirror"),
        Path.Combine("tools", "docs-to-wiki"),
        "vos.Service.Node.Echo",
    ];

    [Fact]
    public void Every_shipped_javascript_component_declares_the_version_the_dotnet_projects_do()
    {
        var expected = DeclaredByDotNetProjects();

        foreach (var package in ShippedPackageFiles())
        {
            using var document = JsonDocument.Parse(File.ReadAllText(package));
            Assert.True(document.RootElement.TryGetProperty("version", out var declared),
                $"{package} declares no version. Add one, or name it in {nameof(NotShipped)} with why.");
            Assert.Equal(expected, declared.GetString());
        }
    }

    /// <summary>A project declaring its own version inherits none, and leaves the rule true of a file
    /// nobody builds from.</summary>
    [Fact]
    public void No_project_declares_a_version_of_its_own()
    {
        var offenders = ProjectFiles()
            .Where(project => Regex.IsMatch(File.ReadAllText(project), @"<Version>"))
            .ToArray();

        Assert.True(offenders.Length == 0,
            $"these declare a version instead of inheriting one: {string.Join(", ", offenders)}");
    }

    /// <summary>Pre-release is the point of the number, not an accident of it: the suffix is what says
    /// the platform makes no compatibility promise, and a release version would say the opposite by
    /// saying nothing.</summary>
    /// <summary>MSBuild ignores comments, so a version left commented out is not the declared one.
    /// Reading it as one failed a repository that agreed with itself.</summary>
    [Fact]
    public void A_commented_out_version_is_not_the_declared_one()
    {
        var props = Path.Combine(RepositoryRoot(), "Directory.Build.props");
        var withComment = File.ReadAllText(props)
            .Replace("<PropertyGroup>", "<PropertyGroup>\n    <!-- <Version>9.9.9</Version> was tried -->");
        var uncommented = Regex.Replace(withComment, "<!--.*?-->", "", RegexOptions.Singleline);

        Assert.DoesNotContain("9.9.9", Regex.Match(uncommented, @"<Version>([^<]+)</Version>").Value);
    }

    [Fact]
    public void The_version_says_it_is_pre_release()
    {
        Assert.Contains("-", DeclaredByDotNetProjects());
    }

    /// <summary>The version a build would actually use. Comments are removed first, because MSBuild
    /// ignores them and a version tried and left commented out above the real one would otherwise be
    /// read as the declared one — failing a repository that agrees with itself.</summary>
    private static string DeclaredByDotNetProjects()
    {
        var props = Path.Combine(RepositoryRoot(), "Directory.Build.props");
        var uncommented = Regex.Replace(File.ReadAllText(props), "<!--.*?-->", "", RegexOptions.Singleline);
        var declared = Regex.Match(uncommented, @"<Version>([^<]+)</Version>");
        Assert.True(declared.Success,
            $"{props} declares no <Version>, so Taproot and every service inherit none.");
        return declared.Groups[1].Value.Trim();
    }

    [Theory]
    [InlineData("*.json")]
    [InlineData("package.json")]
    [InlineData("*.csproj")]
    public void No_build_output_is_searched(string pattern)
    {
        var copies = FilesUnderTheRepository(pattern)
            .Where(path => RelativeSegments(RepositoryRoot(), path).Any(segment => segment is "bin" or "obj"))
            .ToList();

        Assert.True(copies.Count == 0,
            "a copy under bin or obj is as old as the last build of its project: "
            + string.Join("; ", copies.Take(3)));
    }

    private static IEnumerable<string> ShippedPackageFiles() =>
        FilesUnderTheRepository("package.json")
            .Where(package => !NotShipped.Any(
                exempt => package.Contains(Path.DirectorySeparatorChar + exempt + Path.DirectorySeparatorChar)));

    private static IEnumerable<string> ProjectFiles() => FilesUnderTheRepository("*.csproj");

    /// <summary>Source under the repository. Skips hidden directories, which is where a git worktree
    /// keeps a second checkout of everything this would otherwise find twice, and node_modules, which
    /// carries a version file per dependency.</summary>
    /// <remarks>
    /// Build output is skipped because it is a copy, and a copy goes stale: a content file edited in
    /// source stays as it was under bin until something rebuilds that project, so a check reading it
    /// reports a file nobody edited and fails a build nobody broke. A workspace reused between builds
    /// — which is what a self-hosted agent has — is where that bites. Nothing searched here is copied
    /// into build output today, so this is a trap disarmed rather than a break fixed.
    ///
    /// The platform repository carries the same search, and that is where the gap was found. Neither
    /// can reference the other: nothing is packed to a feed, and this repository is public while that
    /// one is not. So a rule learned in one is applied to both by hand, the way vos.Auth.Shared is.
    /// </remarks>
    private static IEnumerable<string> FilesUnderTheRepository(string pattern)
    {
        var root = RepositoryRoot();
        return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "node_modules" + Path.DirectorySeparatorChar))
            .Where(path => !RelativeSegments(root, path)
                .Any(segment => segment.StartsWith('.') || segment is "bin" or "obj"));
    }

    private static string[] RelativeSegments(string root, string path) =>
        Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar);

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
