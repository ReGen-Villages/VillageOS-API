using FluentAssertions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Auth.Shared.Tests;

/// <summary>
/// VosClaimsTests pins each name against the spelling Mycelium mints, and that pin is only worth
/// something while the running code reads the constant. Code that spells a claim itself keeps the old
/// spelling when the pin is corrected, and nothing fails until a caller is refused at run time.
/// </summary>
public class ClaimNamesLiveOnlyInVosClaimsTests
{
    private const string ClaimLiteral = "\"vos:";
    private static readonly string Declaration = Path.Combine("vos.Auth.Shared", "VosClaims.cs");

    [Fact]
    public void Production_code_reads_every_claim_name_from_VosClaims()
    {
        var root = RepositoryRoot.Find();
        var sources = ProductionSources(root).ToList();

        sources.Should().Contain(Declaration,
            "the scan must reach the file that declares the names, or it passes without reading anything");

        var spellingTheirOwn = sources
            .Where(source => source != Declaration)
            .Where(source => File.ReadAllText(Path.Combine(root, source)).Contains(ClaimLiteral))
            .ToList();

        spellingTheirOwn.Should().BeEmpty(
            "a claim name written anywhere but {0} keeps its own spelling when the constant is corrected, "
            + "so the pin guards a value nothing reads at run time", Declaration);
    }

    /// <summary>Every project is named vos.something, and the ones outside Tests are what runs in
    /// production. A test writes the name Mycelium mints on purpose, the way VosClaimsTests does: a
    /// token in a test stands in for one the platform signed, and spelling it out is what makes the
    /// pin mean anything.</summary>
    private static IEnumerable<string> ProductionSources(string root) =>
        new DirectoryInfo(root)
            .EnumerateDirectories("vos.*")
            .SelectMany(project => project.EnumerateFiles("*.cs", SearchOption.AllDirectories))
            .Select(file => Path.GetRelativePath(root, file.FullName))
            .Where(source => !IsCopiedOrInstalled(source));

    private static bool IsCopiedOrInstalled(string relativePath) =>
        relativePath
            .Split(Path.DirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj" or "node_modules");
}
