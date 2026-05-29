using Microsoft.Extensions.Logging;
using vos.ManagedMicroservice.Delta.Helpers;
using vos.ManagedMicroservice.Delta.Models;

namespace vos.ManagedMicroservice.Delta.Services;

/// <summary>
/// Production <see cref="IEndpointSeedProvider"/> that delegates to
/// <see cref="EndpointSeedLoader.LoadGraphDefault"/> — discovers the seed files under the default
/// candidate directories and returns the validated <see cref="EndpointSeedGraph"/>, or throws when
/// none are found or the graph is invalid.
/// </summary>
public sealed class FileEndpointSeedProvider : IEndpointSeedProvider
{
    private readonly ILogger<FileEndpointSeedProvider> _logger;

    public FileEndpointSeedProvider(ILogger<FileEndpointSeedProvider> logger)
    {
        _logger = logger;
    }

    public EndpointSeedGraph LoadGraph() => EndpointSeedLoader.LoadGraphDefault(_logger);
}
