using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using vos.Service.Forage.Configuration;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Forage.Tests;

public class SourceTimeoutAgainstThePlatformsLimitTests
{
    [Fact]
    public async Task StartUp_WhenTheSourceTimeoutIsShorterThanThePassedOnRequestLimit_WarnsNamingBothAndTheSetting()
    {
        var log = await StartedWith(sourceTimeoutSeconds: "20", passedOnRequestLimitSeconds: "30");

        log.Lines.Should().ContainSingle().Which.Should()
            .Contain("--sourceTimeoutSeconds").And.Contain("20 seconds").And.Contain("30 seconds");
    }

    [Fact]
    public async Task StartUp_WhenTheSourceTimeoutEqualsThePassedOnRequestLimit_Warns()
    {
        var log = await StartedWith(sourceTimeoutSeconds: "30", passedOnRequestLimitSeconds: "30");

        log.Lines.Should().ContainSingle().Which.Should().Contain("--sourceTimeoutSeconds");
    }

    [Fact]
    public async Task StartUp_WhenTheSourceTimeoutIsLongerThanThePassedOnRequestLimit_IsSilent()
    {
        var log = await StartedWith(sourceTimeoutSeconds: "60", passedOnRequestLimitSeconds: "30");

        log.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task StartUp_WhenThePlatformGivesNoPassedOnRequestLimit_IsSilent()
    {
        var log = await StartedWith(sourceTimeoutSeconds: "20", passedOnRequestLimitSeconds: null);

        log.Lines.Should().BeEmpty();
    }

    private static async Task<CapturingLogger<ForageLaunchSettings>> StartedWith(
        string sourceTimeoutSeconds, string? passedOnRequestLimitSeconds)
    {
        var log = new CapturingLogger<ForageLaunchSettings>();
        await using var factory = new ForageWebApplicationFactory();
        await using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("SourceTimeoutSeconds", sourceTimeoutSeconds);
            if (passedOnRequestLimitSeconds is not null)
                builder.UseSetting("PassedOnRequestLimitSeconds", passedOnRequestLimitSeconds);
            builder.ConfigureTestServices(services =>
                services.AddSingleton<ILogger<ForageLaunchSettings>>(log));
        });
        using var client = host.CreateClient();
        return log;
    }
}
