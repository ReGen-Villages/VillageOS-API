using FluentAssertions;
using vos.Service.Confluence.Configuration;
using Xunit;

namespace vos.Service.Confluence.Tests;

public class ConfluenceLaunchSettingsTests
{
    private static string[] Required(params string[] extra) =>
        new[] { "--port=5000", "--myceliumUrl=http://localhost:7243" }.Concat(extra).ToArray();

    [Fact]
    public void Parse_WithoutTheServiceSettings_ReturnsNull()
    {
        ConfluenceLaunchSettings.Parse(Array.Empty<string>()).Should().BeNull();
    }

    [Fact]
    public void Parse_DefaultsEverySettingOfItsOwn()
    {
        var settings = ConfluenceLaunchSettings.Parse(Required());

        settings.Should().NotBeNull();
        settings!.FetcherSubdomain.Should().Be(ConfluenceLaunchSettings.DefaultFetcherSubdomain);
        settings.MaxConcurrentSources.Should().Be(ConfluenceLaunchSettings.DefaultMaxConcurrentSources);
        settings.SourceTimeout.Should().Be(ConfluenceLaunchSettings.DefaultSourceTimeout);
    }

    [Fact]
    public void Parse_ReadsEverySettingOfItsOwn()
    {
        var settings = ConfluenceLaunchSettings.Parse(Required(
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
        var settings = ConfluenceLaunchSettings.Parse(Required($"--maxConcurrentSources={raw}"));

        settings!.MaxConcurrentSources.Should().Be(ConfluenceLaunchSettings.DefaultMaxConcurrentSources);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-30")]
    [InlineData("soon")]
    public void Parse_UnusableTimeout_FallsBackToTheDefault(string raw)
    {
        var settings = ConfluenceLaunchSettings.Parse(Required($"--sourceTimeoutSeconds={raw}"));

        settings!.SourceTimeout.Should().Be(ConfluenceLaunchSettings.DefaultSourceTimeout);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    public void Parse_BlankFetcherSubdomain_FallsBackToTheDefault(string raw)
    {
        ConfluenceLaunchSettings.Parse(Required($"--fetcherSubdomain={raw}"))!
            .FetcherSubdomain.Should().Be(ConfluenceLaunchSettings.DefaultFetcherSubdomain);
    }

    [Fact]
    public void Parse_TrimsTheFetcherSubdomain()
    {
        // It goes into a URL path; a stray space would be escaped into the route and match nothing.
        ConfluenceLaunchSettings.Parse(Required("--fetcherSubdomain= fetcher "))!
            .FetcherSubdomain.Should().Be("fetcher");
    }

    [Fact]
    public void UsageMessage_NamesEverySettingOfItsOwn()
    {
        ConfluenceLaunchSettings.UsageMessage.Should()
            .Contain("--fetcherSubdomain")
            .And.Contain("--maxConcurrentSources")
            .And.Contain("--sourceTimeoutSeconds");
    }
}
