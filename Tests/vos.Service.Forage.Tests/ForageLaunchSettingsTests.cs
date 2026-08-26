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
        settings.MaxConcurrentSources.Should().Be(ForageLaunchSettings.DefaultMaxConcurrentSources);
        settings.SourceTimeout.Should().Be(ForageLaunchSettings.DefaultSourceTimeout);
    }

    [Fact]
    public void Parse_ReadsEverySettingOfItsOwn()
    {
        var settings = ForageLaunchSettings.Parse(Required(
            "--fetcherSubdomain=fetcher", "--maxConcurrentSources=9", "--sourceTimeoutSeconds=5"));

        settings!.FetcherSubdomain.Should().Be("fetcher");
        settings.MaxConcurrentSources.Should().Be(9);
        settings.SourceTimeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("many")]
    [InlineData("")]
    public void Parse_UnusableConcurrency_FallsBackToTheDefault(string raw)
    {
        // A bound of zero would stall the run outright and a negative one is meaningless; neither
        // should start a service that then does nothing.
        var settings = ForageLaunchSettings.Parse(Required($"--maxConcurrentSources={raw}"));

        settings!.MaxConcurrentSources.Should().Be(ForageLaunchSettings.DefaultMaxConcurrentSources);
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
    public void UsageMessage_NamesEverySettingOfItsOwn()
    {
        ForageLaunchSettings.UsageMessage.Should()
            .Contain("--fetcherSubdomain")
            .And.Contain("--maxConcurrentSources")
            .And.Contain("--sourceTimeoutSeconds");
    }
}
