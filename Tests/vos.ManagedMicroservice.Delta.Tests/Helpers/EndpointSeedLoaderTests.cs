using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.ManagedMicroservice.Delta.Helpers;
using Xunit;

namespace vos.ManagedMicroservice.Delta.Tests.Helpers;

public class EndpointSeedLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public EndpointSeedLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "delta-seed-loader-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteSeed(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static readonly string ValidSeedJson = """
        {
          "name": "Endpoint",
          "properties": {
            "url": "https://default.example/",
            "httpMethod": "GET"
          }
        }
        """;

    [Fact]
    public void Load_FirstCandidateValid_ReturnsSeed()
    {
        var path = WriteSeed("seed.json", ValidSeedJson);

        var seed = EndpointSeedLoader.Load(new[] { path }, NullLogger.Instance);

        seed.Name.Should().Be("Endpoint");
        seed.Properties.Should().NotBeNull();
        seed.Properties!.Should().ContainKey("url");
    }

    [Fact]
    public void Load_FirstCandidateMissing_FallsBackToSecond()
    {
        var missing = Path.Combine(_tempDir, "does-not-exist.json");
        var valid = WriteSeed("second.json", ValidSeedJson);

        var seed = EndpointSeedLoader.Load(new[] { missing, valid }, NullLogger.Instance);

        seed.Name.Should().Be("Endpoint");
    }

    [Fact]
    public void Load_FirstCandidateMalformed_LogsWarningAndFallsBackToSecond()
    {
        var malformed = WriteSeed("malformed.json", "{ this is not valid json");
        var valid = WriteSeed("valid.json", ValidSeedJson);

        var seed = EndpointSeedLoader.Load(new[] { malformed, valid }, NullLogger.Instance);

        seed.Name.Should().Be("Endpoint");
    }

    [Fact]
    public void Load_FirstCandidateValidJsonButEmptyName_FallsBackToSecond()
    {
        // A seed with empty Name fails the validity check and the loader moves on.
        var emptyName = WriteSeed("empty-name.json", """{ "name": "", "properties": {} }""");
        var valid = WriteSeed("valid.json", ValidSeedJson);

        var seed = EndpointSeedLoader.Load(new[] { emptyName, valid }, NullLogger.Instance);

        seed.Name.Should().Be("Endpoint");
    }

    [Fact]
    public void Load_AllCandidatesMissing_ThrowsInvalidOperationException()
    {
        var missing1 = Path.Combine(_tempDir, "a.json");
        var missing2 = Path.Combine(_tempDir, "b.json");

        var act = () => EndpointSeedLoader.Load(new[] { missing1, missing2 }, NullLogger.Instance);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Could not load a valid Endpoint seed*");
    }

    [Fact]
    public void Load_AllCandidatesMalformed_ThrowsInvalidOperationException()
    {
        var bad1 = WriteSeed("bad1.json", "garbage");
        var bad2 = WriteSeed("bad2.json", "{");

        var act = () => EndpointSeedLoader.Load(new[] { bad1, bad2 }, NullLogger.Instance);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Could not load a valid Endpoint seed*");
    }

    [Fact]
    public void Load_EmptyCandidateList_ThrowsInvalidOperationException()
    {
        var act = () => EndpointSeedLoader.Load(Array.Empty<string>(), NullLogger.Instance);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void DefaultCandidatePaths_ContainsExpectedThree()
    {
        var paths = EndpointSeedLoader.DefaultCandidatePaths.ToList();

        paths.Should().HaveCount(3);
        paths.Should().AllSatisfy(p => p.Should().EndWith("seed.json"));
    }
}
