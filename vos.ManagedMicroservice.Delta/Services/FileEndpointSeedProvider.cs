using Microsoft.Extensions.Logging;
using vos.ManagedMicroservice.Delta.Helpers;
using vos.ManagedMicroservice.Delta.Models;

namespace vos.ManagedMicroservice.Delta.Services;

/// <summary>
/// Production <see cref="IEndpointSeedProvider"/> that delegates to
/// <see cref="EndpointSeedLoader.LoadDefault"/> — walks the default candidate paths under
/// <c>AppContext.BaseDirectory</c> and returns the first valid seed, or throws when none parse.
/// </summary>
public sealed class FileEndpointSeedProvider : IEndpointSeedProvider
{
    private readonly ILogger<FileEndpointSeedProvider> _logger;

    public FileEndpointSeedProvider(ILogger<FileEndpointSeedProvider> logger)
    {
        _logger = logger;
    }

    public RegisterEndpointRequest LoadSeed() => EndpointSeedLoader.LoadDefault(_logger);
}
