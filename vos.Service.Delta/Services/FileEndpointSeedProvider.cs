using Microsoft.Extensions.Logging;
using vos.Service.Delta.Helpers;
using vos.Service.Delta.Models;

namespace vos.Service.Delta.Services;

// Production IEndpointSeedProvider that delegates to
// EndpointSeedLoader.LoadGraphDefault — discovers the seed files under the default
// candidate directories and returns the validated EndpointSeedGraph, or throws when
// none are found or the graph is invalid.
public sealed class FileEndpointSeedProvider : IEndpointSeedProvider
{
    private readonly ILogger<FileEndpointSeedProvider> _logger;

    public FileEndpointSeedProvider(ILogger<FileEndpointSeedProvider> logger)
    {
        _logger = logger;
    }

    public EndpointSeedGraph LoadGraph() => EndpointSeedLoader.LoadGraphDefault(_logger);
}
