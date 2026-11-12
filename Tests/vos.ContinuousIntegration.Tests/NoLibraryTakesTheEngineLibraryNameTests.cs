using System.Text.RegularExpressions;
using vos.Tests.Shared;
using Xunit;

namespace vos.ContinuousIntegration.Tests;

// The engine ships its own library of this name. A program that runs the engine beside these services
// gets both files in one folder under one name, and keeps whichever was written last.
public class NoLibraryTakesTheEngineLibraryNameTests
{
    private const string EngineLibraryName = "vos.Auth.Shared";

    [Fact]
    public void No_project_builds_a_file_named_after_the_engine_library()
    {
        var offenders = SourceFiles("*.csproj")
            .Where(project => BuiltFileName(project) == EngineLibraryName)
            .ToArray();

        Assert.True(offenders.Length == 0,
            $"these build {EngineLibraryName}.dll, the engine library's name: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void No_source_file_declares_the_engine_library_namespace()
    {
        var declaration = new Regex(@"^\s*namespace\s+" + Regex.Escape(EngineLibraryName) + @"\b", RegexOptions.Multiline);
        var offenders = SourceFiles("*.cs")
            .Where(source => declaration.IsMatch(File.ReadAllText(source)))
            .ToArray();

        Assert.True(offenders.Length == 0,
            $"these declare the engine library's namespace {EngineLibraryName}: {string.Join(", ", offenders)}");
    }

    private static string BuiltFileName(string project)
    {
        var declared = Regex.Match(File.ReadAllText(project), @"<AssemblyName>([^<]+)</AssemblyName>");
        return declared.Success ? declared.Groups[1].Value.Trim() : Path.GetFileNameWithoutExtension(project);
    }

    private static IEnumerable<string> SourceFiles(string pattern)
    {
        var root = RepositoryRoot.Find();
        return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar)
                .Any(segment => segment.StartsWith('.') || segment is "bin" or "obj" or "node_modules"));
    }
}
