using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Delta.Helpers;
using Xunit;

namespace vos.Service.Delta.Tests.Helpers;

// Unit tests for EndpointSeedLoader.LoadGraph — loading a single model-seed document
// (things + relationships) from the first existing candidate path and assembling it into a validated
// vos.Service.Delta.Models.EndpointSeedGraph. The graph's own validation
// rules are covered by Models/EndpointSeedGraphTests; here we pin discovery, parse failures,
// and that validation errors propagate.
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

    private const string SingleThingModel = """
        {
          "name": "Endpoint Templates",
          "things": [ { "name": "Endpoint", "properties": { "url": "https://default.example/", "httpMethod": "GET" } } ],
          "relationships": []
        }
        """;

    private const string TwoTemplateModel = """
        {
          "name": "Endpoint Templates",
          "things": [
            { "name": "Endpoint", "properties": { "url": "", "httpMethod": "GET" } },
            { "name": "EsriEndpoint", "properties": { "httpMethod": "POST" } }
          ],
          "relationships": [ { "subject": "EsriEndpoint", "predicate": "is", "target": "Endpoint" } ]
        }
        """;

    [Fact]
    public void LoadGraph_SingleThingModel_LoadsOneNodeGraph()
    {
        var path = WriteSeed("seed.json", SingleThingModel);

        var graph = EndpointSeedLoader.LoadGraph(new[] { path }, NullLogger.Instance);

        graph.Root.Name.Should().Be("Endpoint");
        graph.Templates.Should().HaveCount(1);
    }

    [Fact]
    public void LoadGraph_MultiTemplateModel_LoadsAllKeyedByName()
    {
        var path = WriteSeed("seed.json", TwoTemplateModel);

        var graph = EndpointSeedLoader.LoadGraph(new[] { path }, NullLogger.Instance);

        graph.Templates.Keys.Should().BeEquivalentTo(new[] { "Endpoint", "EsriEndpoint" });
        graph.Root.Name.Should().Be("Endpoint");
        graph.ParentName("EsriEndpoint").Should().Be("Endpoint");
    }

    [Fact]
    public void LoadGraph_FirstCandidateMissing_FallsBackToSecond()
    {
        var missing = Path.Combine(_tempDir, "does-not-exist.json");
        var present = WriteSeed("seed.json", SingleThingModel);

        var graph = EndpointSeedLoader.LoadGraph(new[] { missing, present }, NullLogger.Instance);

        graph.Root.Name.Should().Be("Endpoint");
    }

    [Fact]
    public void LoadGraph_MalformedSeed_Throws()
    {
        var path = WriteSeed("seed.json", "{ this is not valid json");

        var act = () => EndpointSeedLoader.LoadGraph(new[] { path }, NullLogger.Instance);

        act.Should().Throw<InvalidOperationException>().WithMessage("*parse*");
    }

    [Fact]
    public void LoadGraph_InvalidGraph_PropagatesValidationError()
    {
        // Two roots (no 'is' between them) → EndpointSeedGraph.Build rejects.
        var twoRoots = """
            { "things": [ { "name": "Endpoint", "properties": {} }, { "name": "Other", "properties": {} } ],
              "relationships": [] }
            """;
        var path = WriteSeed("seed.json", twoRoots);

        var act = () => EndpointSeedLoader.LoadGraph(new[] { path }, NullLogger.Instance);

        act.Should().Throw<InvalidOperationException>().WithMessage("*multiple root templates*");
    }

    [Fact]
    public void LoadGraph_NoSeedAnywhere_Throws()
    {
        var missing1 = Path.Combine(_tempDir, "a.json");
        var missing2 = Path.Combine(_tempDir, "b.json");

        var act = () => EndpointSeedLoader.LoadGraph(new[] { missing1, missing2 }, NullLogger.Instance);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Could not load a valid Endpoint seed*");
    }

    [Fact]
    public void DefaultCandidatePaths_ContainsExpectedThree()
    {
        var paths = EndpointSeedLoader.DefaultCandidatePaths.ToList();

        paths.Should().HaveCount(3);
        paths.Should().AllSatisfy(p => p.Should().EndWith("seed.json"));
    }
}
