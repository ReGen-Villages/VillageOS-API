using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Forage.Configuration;
using vos.Service.Forage.Services;
using vos.Service.Shared.Configuration;
using Xunit;

namespace vos.Service.Forage.Tests.Services;

public class SourceFetchingTests
{
    private static ServiceProvider Wired(int windowDays = 365)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddHttpClient();
        services.AddSourceFetching(
            new ForageLaunchSettings(
                new ServiceLaunchSettings(7100, "http://localhost:7243"),
                ForageLaunchSettings.DefaultFetcherSubdomain,
                ForageLaunchSettings.DefaultMaxConcurrentSources,
                ForageLaunchSettings.DefaultSourceTimeout,
                windowDays),
            "http://localhost:7243",
            "token",
            apiKey: null);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void A_run_asking_for_a_fetcher_gets_one_that_slices_a_long_reach()
    {
        using var provider = Wired();

        provider.GetRequiredService<ISourceFetcher>().Should().BeOfType<SlicedWindowFetcher>();
    }

    [Fact]
    public void The_body_reader_and_the_fetcher_it_slices_are_the_same_registration()
    {
        using var provider = Wired();

        provider.GetRequiredService<IEndpointBodyReader>()
            .Should().BeSameAs(provider.GetRequiredService<EndpointServiceSourceFetcher>());
    }
}
