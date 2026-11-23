using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using vos.Service.Forage.Configuration;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Forage.Tests;

public class SourceTimeoutAgainstThePlatformsLimitTests
{
    private static readonly int SourceTimeoutSeconds = (int)ForageLaunchSettings.SourceTimeout.TotalSeconds;

    [Fact]
    public async Task StartUp_WhenThePassedOnRequestLimitIsLongerThanTheSourceTimeout_WarnsNamingBoth()
    {
        var log = await StartedWith(passedOnRequestLimitSeconds: $"{SourceTimeoutSeconds + 30}");

        log.Lines.Should().ContainSingle().Which.Should()
            .Contain($"{SourceTimeoutSeconds} seconds").And.Contain($"{SourceTimeoutSeconds + 30} seconds");
    }

    [Fact]
    public async Task StartUp_WhenThePassedOnRequestLimitEqualsTheSourceTimeout_Warns()
    {
        var log = await StartedWith(passedOnRequestLimitSeconds: $"{SourceTimeoutSeconds}");

        log.Lines.Should().ContainSingle();
    }

    [Fact]
    public async Task StartUp_WhenThePassedOnRequestLimitIsShorterThanTheSourceTimeout_IsSilent()
    {
        var log = await StartedWith(passedOnRequestLimitSeconds: $"{SourceTimeoutSeconds - 30}");

        log.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task StartUp_WhenThePlatformGivesNoPassedOnRequestLimit_IsSilent()
    {
        var log = await StartedWith(passedOnRequestLimitSeconds: null);

        log.Lines.Should().BeEmpty();
    }

    private static async Task<CapturingLogger<ForageLaunchSettings>> StartedWith(string? passedOnRequestLimitSeconds)
    {
        var log = new CapturingLogger<ForageLaunchSettings>();
        await using var factory = new ForageWebApplicationFactory();
        await using var host = factory.WithWebHostBuilder(builder =>
        {
            if (passedOnRequestLimitSeconds is not null)
                builder.UseSetting("PassedOnRequestLimitSeconds", passedOnRequestLimitSeconds);
            builder.ConfigureTestServices(services =>
                services.AddSingleton<ILogger<ForageLaunchSettings>>(log));
        });
        using var client = host.CreateClient();
        return log;
    }
}
