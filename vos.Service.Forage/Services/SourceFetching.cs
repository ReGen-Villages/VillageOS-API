using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using vos.Service.Forage.Configuration;

namespace vos.Service.Forage.Services;

// How a discovery run reaches a source: the fetcher that asks the broker to forward the call, with the
// slicing in front of it.
//
// Registered here rather than in the entry point so the arrangement can be asked a question. Wired
// inline, the slicing could be lifted out and every test would still pass — the fetcher's own tests
// prove what it does when something wraps it, not that anything does.
public static class SourceFetching
{
    public static IServiceCollection AddSourceFetching(
        this IServiceCollection services,
        ForageLaunchSettings launchSettings,
        string myceliumUrl,
        string? serviceToken,
        string? apiKey)
    {
        services.AddSingleton(provider => new EndpointServiceSourceFetcher(
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<ILogger<EndpointServiceSourceFetcher>>(),
            myceliumUrl,
            serviceToken,
            launchSettings.FetcherSubdomain,
            launchSettings.SourceTimeout,
            apiKey));

        services.AddSingleton<ISourceFetcher>(provider => new SlicedWindowFetcher(
            provider.GetRequiredService<EndpointServiceSourceFetcher>(),
            launchSettings.SourceWindowDays,
            provider.GetRequiredService<ILogger<SlicedWindowFetcher>>()));

        return services.AddSingleton<IEndpointBodyReader>(
            provider => provider.GetRequiredService<EndpointServiceSourceFetcher>());
    }
}
