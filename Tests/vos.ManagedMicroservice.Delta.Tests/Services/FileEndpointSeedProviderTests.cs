using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.ManagedMicroservice.Delta.Services;
using Xunit;

namespace vos.ManagedMicroservice.Delta.Tests.Services;

/// <summary>
/// Unit tests for the production <see cref="FileEndpointSeedProvider"/> — the thin wrapper around
/// <c>EndpointSeedLoader.LoadGraphDefault</c>. The loader's discovery decision tree is covered by
/// <c>Helpers/EndpointSeedLoaderTests</c>; here we pin that the wrapper delegates to it (happy path
/// via a model <c>seed.json</c> in the base directory) and surfaces the same throw contract when no
/// seed is reachable.
/// </summary>
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
