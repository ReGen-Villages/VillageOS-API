using FluentAssertions;
using vos.Service.Forage.Configuration;
using Xunit;

namespace vos.Service.Forage.Tests;

public class ForageLaunchSettingsTests
{
    private static string[] Required(params string[] extra) =>
        new[] { "--port=5000", "--myceliumUrl=http://localhost:7243" }.Concat(extra).ToArray();

    [Fact]
    public void Parse_WithoutTheServiceSettings_ReturnsNull()
    {
        ForageLaunchSettings.Parse(Array.Empty<string>()).Should().BeNull();
    }

    [Fact]
    public void Parse_DefaultsEverySettingOfItsOwn()
    {
        var settings = ForageLaunchSettings.Parse(Required());

        settings.Should().NotBeNull();
        settings!.FetcherSubdomain.Should().Be(ForageLaunchSettings.DefaultFetcherSubdomain);
        settings.SourceTimeout.Should().Be(ForageLaunchSettings.DefaultSourceTimeout);
        settings.SourceWindowDays.Should().Be(ForageLaunchSettings.DefaultSourceWindowDays);
    }

    [Fact]
    public void Parse_ReadsEverySettingOfItsOwn()
    {
        var settings = ForageLaunchSettings.Parse(Required("--fetcherSubdomain=fetcher", "--sourceTimeoutSeconds=5", "--sourceWindowDays=30"));

        settings!.FetcherSubdomain.Should().Be("fetcher");
        settings.SourceTimeout.Should().Be(TimeSpan.FromSeconds(5));
        settings.SourceWindowDays.Should().Be(30);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-30")]
    [InlineData("soon")]
    public void Parse_UnusableTimeout_FallsBackToTheDefault(string raw)
    {
        var settings = ForageLaunchSettings.Parse(Required($"--sourceTimeoutSeconds={raw}"));

        settings!.SourceTimeout.Should().Be(ForageLaunchSettings.DefaultSourceTimeout);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("a year")]
    [InlineData("")]
    public void Parse_UnusableSourceWindow_FallsBackToTheDefault(string raw)
    {
        ForageLaunchSettings.Parse(Required($"--sourceWindowDays={raw}"))!
            .SourceWindowDays.Should().Be(ForageLaunchSettings.DefaultSourceWindowDays);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    public void Parse_BlankFetcherSubdomain_FallsBackToTheDefault(string raw)
    {
        ForageLaunchSettings.Parse(Required($"--fetcherSubdomain={raw}"))!
            .FetcherSubdomain.Should().Be(ForageLaunchSettings.DefaultFetcherSubdomain);
    }

    [Fact]
    public void Parse_TrimsTheFetcherSubdomain()
    {
        // It goes into a URL path; a stray space would be escaped into the route and match nothing.
        ForageLaunchSettings.Parse(Required("--fetcherSubdomain= fetcher "))!
            .FetcherSubdomain.Should().Be("fetcher");
    }

    [Fact]
    public void UsageMessage_NamesEverySettingOfItsOwnAndNoneOfTheRemovedRun()
    {
        ForageLaunchSettings.UsageMessage.Should()
            .Contain("--fetcherSubdomain")
            .And.Contain("--sourceTimeoutSeconds")
            .And.Contain("--sourceWindowDays")
            .And.NotContain("--maxConcurrentSources")
            .And.NotContain("--attemptsPerSlice");
    }
}
