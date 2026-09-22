using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Delta.Services;
using Xunit;

namespace vos.Service.Delta.Tests.Services;

// Unit tests for the production FileEndpointSeedProvider — the thin wrapper around
// EndpointSeedLoader.LoadGraphDefault. The loader's discovery decision tree is covered by
// Helpers/EndpointSeedLoaderTests; here we pin that the wrapper delegates to it (happy path
// via a model seed.json in the base directory) and surfaces the same throw contract when no
// seed is reachable.
public class FileEndpointSeedProviderTests : IDisposable
{
    private readonly string _seedPath = Path.Combine(AppContext.BaseDirectory, "seed.json");
    private readonly string? _backup;

    public FileEndpointSeedProviderTests()
    {
        if (File.Exists(_seedPath))
        {
            _backup = _seedPath + ".bak-" + Guid.NewGuid().ToString("N");
            File.Move(_seedPath, _backup);
        }
    }

    public void Dispose()
    {
        if (File.Exists(_seedPath))
            File.Delete(_seedPath);
        if (_backup != null && File.Exists(_backup))
            File.Move(_backup, _seedPath);
    }

    [Fact]
    public void LoadGraph_ValidModelSeed_ReturnsParsedRoot()
    {
        File.WriteAllText(_seedPath, """
            {
              "name": "Endpoint Templates",
              "things": [ { "name": "Endpoint", "properties": { "url": "https://default.example/", "httpMethod": "GET" } } ],
              "relationships": []
            }
            """);

        var provider = new FileEndpointSeedProvider(NullLogger<FileEndpointSeedProvider>.Instance);
        var graph = provider.LoadGraph();

        graph.Root.Name.Should().Be("Endpoint");
        graph.Root.Properties.Should().ContainKey("url");
        graph.Root.Properties!.Should().ContainKey("httpMethod");
    }

    [Fact]
    public void LoadGraph_NoSeedAnywhere_ThrowsInvalidOperationException()
    {
        // _seedPath is guaranteed clean by ctor + Dispose. The other candidate paths
        // (CurrentDirectory, BaseDirectory/../../..) won't have a seed.json in a normal test run.
        var provider = new FileEndpointSeedProvider(NullLogger<FileEndpointSeedProvider>.Instance);

        var act = () => provider.LoadGraph();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Could not load a valid Endpoint seed*");
    }
}
