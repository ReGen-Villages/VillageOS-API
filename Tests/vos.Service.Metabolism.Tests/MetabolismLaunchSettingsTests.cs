using FluentAssertions;
using Microsoft.Extensions.Configuration;
using vos.Service.Metabolism.Configuration;
using Xunit;

namespace vos.Service.Metabolism.Tests;

// The settings every service shares are covered once in vos.Service.Shared.Tests. These cover the
// mode, which only Metabolism has, and that the shared settings reach the caller.
public class MetabolismLaunchSettingsTests
{
    private static readonly string[] RequiredFlags =
        ["--port=7111", "--myceliumUrl=https://localhost:7243"];

    private static string[] FlagsWith(params string[] extra) => [.. RequiredFlags, .. extra];

    private static IConfiguration ConfigurationFrom(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            pairs.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))).Build();

    [Theory]
    [InlineData(MetabolismLaunchSettings.ConsumesMode)]
    [InlineData(MetabolismLaunchSettings.ProducesMode)]
    public void Parse_AcceptsEitherMode(string mode)
    {
        var result = MetabolismLaunchSettings.Parse(FlagsWith($"--mode={mode}"));

        result.Should().NotBeNull();
        result!.Mode.Should().Be(mode);
    }

    [Theory]
    [InlineData("CONSUMES")]
    [InlineData("Produces")]
    [InlineData("cOnSuMeS")]
    public void Parse_LowercasesTheMode(string mode)
    {
        var result = MetabolismLaunchSettings.Parse(FlagsWith($"--mode={mode}"));

        result.Should().NotBeNull();
        result!.Mode.Should().Be(mode.ToLowerInvariant());
    }

    [Fact]
    public void Parse_WhenModeMissing_ReturnsNull() =>
        MetabolismLaunchSettings.Parse(RequiredFlags).Should().BeNull();

    [Theory]
    [InlineData("")]
    [InlineData("neither")]
    [InlineData("consume")]
    public void Parse_WithAModeTheServiceCannotRun_ReturnsNull(string mode) =>
        MetabolismLaunchSettings.Parse(FlagsWith($"--mode={mode}")).Should().BeNull();

    [Fact]
    public void Parse_WhenACommonSettingIsMissing_ReturnsNull() =>
        MetabolismLaunchSettings.Parse(["--mode=consumes"]).Should().BeNull();

    [Fact]
    public void Parse_CarriesTheCommonSettingsThrough()
    {
        var result = MetabolismLaunchSettings.Parse(
            FlagsWith("--mode=consumes", "--token=my.jwt.token", "--issuer=VillageOS"));

        result.Should().NotBeNull();
        result!.Service.Port.Should().Be(7111);
        result.Service.MyceliumUrl.Should().Be("https://localhost:7243");
        result.Service.Token.Should().Be("my.jwt.token");
        result.Service.Issuer.Should().Be("VillageOS");
    }

    [Fact]
    public void Parse_ReadsTheModeFromConfiguration()
    {
        var configuration = ConfigurationFrom(
            ("Port", "7100"), ("MyceliumUrl", "http://mycelium"), ("Mode", "produces"));

        var result = MetabolismLaunchSettings.Parse(Array.Empty<string>(), configuration);

        result.Should().NotBeNull();
        result!.Mode.Should().Be(MetabolismLaunchSettings.ProducesMode);
    }

    [Fact]
    public void Parse_WhenTheModeFlagAndConfigurationDisagree_TheFlagWins()
    {
        var configuration = ConfigurationFrom(("Mode", "produces"));

        var result = MetabolismLaunchSettings.Parse(FlagsWith("--mode=consumes"), configuration);

        result.Should().NotBeNull();
        result!.Mode.Should().Be(MetabolismLaunchSettings.ConsumesMode);
    }

    [Fact]
    public void UsageMessage_NamesTheModeFlagAndBothValues()
    {
        var usage = MetabolismLaunchSettings.UsageMessage;

        usage.Should().Contain("--mode")
            .And.Contain(MetabolismLaunchSettings.ConsumesMode)
            .And.Contain(MetabolismLaunchSettings.ProducesMode)
            .And.Contain("--myceliumUrl");
    }
}
