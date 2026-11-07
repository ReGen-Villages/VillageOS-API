using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Forage.Configuration;
using vos.Service.Forage.Helpers;
using vos.Service.Forage.Services;
using vos.Service.Shared.Configuration;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Forage.Tests.Services;

public class SourceFetchingTests
{
    private static ServiceProvider Wired(
        int windowDays = 365,
        int attemptsPerSlice = ForageLaunchSettings.DefaultAttemptsPerSlice,
        HttpMessageHandler? broker = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddHttpClient();
        if (broker is not null)
            services.AddSingleton<IHttpClientFactory>(new PerCallHttpClientFactory(broker));
        services.AddSingleton(TimeProvider.System);
        services.AddSourceFetching(
            new ForageLaunchSettings(
                new ServiceLaunchSettings(7100, "http://localhost:7243"),
                ForageLaunchSettings.DefaultFetcherSubdomain,
                ForageLaunchSettings.DefaultMaxConcurrentSources,
                ForageLaunchSettings.DefaultSourceTimeout,
                windowDays,
                attemptsPerSlice),
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

    [Fact]
    public async Task A_slice_the_broker_times_out_is_asked_for_as_often_as_the_launch_settings_allow()
    {
        var broker = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.GatewayTimeout)
        {
            Content = new StringContent("""{"error":"Service timeout"}""", Encoding.UTF8, "application/json"),
        });
        using var provider = Wired(windowDays: 1, attemptsPerSlice: 5, broker: broker);

        var outcome = await provider.GetRequiredService<ISourceFetcher>().FetchAsync(
            Guid.NewGuid(), "Open-Meteo climate history", "climate-history",
            new Dictionary<string, string>
            {
                [CoveringSourceResolver.WindowStartPlaceholder] = "2026-01-01",
                [CoveringSourceResolver.WindowEndPlaceholder] = "2026-01-02",
            },
            default);

        outcome.Resolved.Should().BeFalse();
        broker.Requests.Should().HaveCount(5);
    }
}
