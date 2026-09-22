using Microsoft.Extensions.Logging;
using vos.Service.Delta.Helpers;
using vos.Service.Delta.Models;

namespace vos.Service.Delta.Services;

public sealed class FileEndpointSeedProvider : IEndpointSeedProvider
{
    private readonly ILogger<FileEndpointSeedProvider> _logger;

    public FileEndpointSeedProvider(ILogger<FileEndpointSeedProvider> logger)
    {
        _logger = logger;
    }

    public EndpointSeedGraph LoadGraph() => EndpointSeedLoader.LoadGraphDefault(_logger);
}
