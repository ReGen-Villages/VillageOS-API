using FluentAssertions;
using vos.Service.Metabolism.Configuration;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Metabolism.Tests;

// The settings every service shares are covered once in vos.Service.Shared.Tests. These cover the
// mode, which only Metabolism has, and that the shared settings reach the caller.
public class MetabolismLaunchSettingsTests
{
    [Theory]
    [InlineData("consumes")]
    [InlineData("produces")]
    public void Parse_AcceptsEitherDirection(string argument)
    {
        var result = MetabolismLaunchSettings.Parse(LaunchFlags.With($"--mode={argument}"));

        result.Should().NotBeNull();
        result!.Direction.LaunchArgument.Should().Be(argument);
    }

    // The direction decided the arithmetic through a word each reader compared
    // against its own literal, so a differently-cased argument had to survive as the same direction.
    [Theory]
    [InlineData("CONSUMES")]
    [InlineData("cOnSuMeS")]
    public void Parse_ResolvesADifferentlyCasedArgumentToTheSameDirection(string argument)
    {
        var result = MetabolismLaunchSettings.Parse(LaunchFlags.With($"--mode={argument}"));

        result.Should().NotBeNull();
        result!.Direction.Should().BeSameAs(ResourceDirection.Consumes);
        result.Direction.TrackingPropertyName.Should().Be("total_consumed");
        result.Direction.PoolAction.Should().Be("decrements");
    }

    [Fact]
    public void Parse_WhenModeMissing_ReturnsNull() =>
        MetabolismLaunchSettings.Parse(LaunchFlags.Required).Should().BeNull();

    [Theory]
    [InlineData("")]
    [InlineData("neither")]
    [InlineData("consume")]
    public void Parse_WithAModeTheServiceCannotRun_ReturnsNull(string mode) =>
        MetabolismLaunchSettings.Parse(LaunchFlags.With($"--mode={mode}")).Should().BeNull();

    [Fact]
    public void Parse_WhenACommonSettingIsMissing_ReturnsNull() =>
        MetabolismLaunchSettings.Parse(["--mode=consumes"]).Should().BeNull();

    [Fact]
    public void Parse_CarriesTheCommonSettingsThrough()
    {
        var result = MetabolismLaunchSettings.Parse(
            LaunchFlags.With("--mode=consumes", "--issuer=VillageOS"));

        result.Should().NotBeNull();
        result!.Service.Port.Should().Be(LaunchFlags.Port);
        result.Service.MyceliumUrl.Should().Be(LaunchFlags.MyceliumUrl);
        result.Service.Issuer.Should().Be("VillageOS");
    }

    [Fact]
    public void Parse_ReadsTheModeFromConfiguration()
    {
        var configuration = LaunchFlags.ConfigurationFrom(
            ("Port", "7100"), ("MyceliumUrl", "http://mycelium"), ("Mode", "produces"));

        var result = MetabolismLaunchSettings.Parse(Array.Empty<string>(), configuration);

        result.Should().NotBeNull();
        result!.Direction.Should().BeSameAs(ResourceDirection.Produces);
    }

    [Fact]
    public void Parse_WhenTheModeFlagAndConfigurationDisagree_TheFlagWins()
    {
        var configuration = LaunchFlags.ConfigurationFrom(("Mode", "produces"));

        var result = MetabolismLaunchSettings.Parse(LaunchFlags.With("--mode=consumes"), configuration);

        result.Should().NotBeNull();
        result!.Direction.Should().BeSameAs(ResourceDirection.Consumes);
    }

    [Fact]
    public void UsageMessage_NamesTheModeFlagAndBothValues()
    {
        var usage = MetabolismLaunchSettings.UsageMessage;

        usage.Should().Contain("--mode")
            .And.Contain(ResourceDirection.Consumes.LaunchArgument)
            .And.Contain(ResourceDirection.Produces.LaunchArgument)
            .And.Contain("--myceliumUrl");
    }
}
