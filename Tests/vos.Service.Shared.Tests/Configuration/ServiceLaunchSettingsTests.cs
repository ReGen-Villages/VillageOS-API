using FluentAssertions;
using Microsoft.Extensions.Configuration;
using vos.Service.Shared.Configuration;
using Xunit;

namespace vos.Service.Shared.Tests.Configuration;

public class ServiceLaunchSettingsTests
{
    private const string SoftHyphen = "­";
    private const string ZeroWidthJoiner = "‍";

    private static IConfiguration ConfigurationFrom(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            pairs.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))).Build();

    [Fact]
    public void Parse_WithBothRequiredFlags_ReadsThem()
    {
        var result = ServiceLaunchSettings.Parse(
            ["--port=7111", "--myceliumUrl=https://localhost:7243"]);

        result.Should().NotBeNull();
        result!.Port.Should().Be(7111);
        result.MyceliumUrl.Should().Be("https://localhost:7243");
    }

    [Fact]
    public void Parse_WithoutOptionalFlags_LeavesThemUnset()
    {
        var result = ServiceLaunchSettings.Parse(
            ["--port=7111", "--myceliumUrl=https://localhost:7243"]);

        result.Should().NotBeNull();
        result!.Token.Should().BeNull();
        result.SigningKey.Should().BeNull();
        result.Issuer.Should().BeNull();
        result.Audience.Should().BeNull();
    }

    [Fact]
    public void Parse_WithEveryOptionalFlag_ReadsThemAll()
    {
        var result = ServiceLaunchSettings.Parse([
            "--port=7111", "--myceliumUrl=https://localhost:7243",
            "--issuer=VillageOS", "--audience=VillageOSClients"
        ]);

        result.Should().NotBeNull();
        result!.Issuer.Should().Be("VillageOS");
        result.Audience.Should().Be("VillageOSClients");
    }

    [Fact]
    public void Parse_WhenACredentialIsGivenOnTheCommandLine_IgnoresIt()
    {
        var result = ServiceLaunchSettings.Parse([
            "--port=7111", "--myceliumUrl=https://localhost:7243",
            "--token=my.jwt.token", "--signingKey=c29tZWtleQ=="
        ]);

        result.Should().NotBeNull();
        result!.Token.Should().BeNull();
        result.SigningKey.Should().BeNull();
    }

    [Fact]
    public void Parse_WhenACredentialIsOnTheCommandLineAndInConfiguration_TakesTheConfiguredOne()
    {
        var configuration = ConfigurationFrom(
            ("Token", "configured-token"), ("SigningKey", "configured-key"));

        var result = ServiceLaunchSettings.Parse([
            "--port=7111", "--myceliumUrl=https://localhost:7243",
            "--token=flag-token", "--signingKey=flag-key"
        ], configuration);

        result.Should().NotBeNull();
        result!.Token.Should().Be("configured-token");
        result.SigningKey.Should().Be("configured-key");
    }

    [Fact]
    public void Parse_WhenPortMissing_ReturnsNull() =>
        ServiceLaunchSettings.Parse(["--myceliumUrl=https://localhost:7243"]).Should().BeNull();

    [Fact]
    public void Parse_WhenMyceliumUrlMissing_ReturnsNull() =>
        ServiceLaunchSettings.Parse(["--port=7111"]).Should().BeNull();

    [Fact]
    public void Parse_WhenMyceliumUrlIsEmpty_ReturnsNull() =>
        ServiceLaunchSettings.Parse(["--port=7111", "--myceliumUrl="]).Should().BeNull();

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("65536")]
    [InlineData("not-a-number")]
    [InlineData("")]
    public void Parse_WithUnusablePort_ReturnsNull(string port) =>
        ServiceLaunchSettings.Parse([$"--port={port}", "--myceliumUrl=https://localhost:7243"])
            .Should().BeNull();

    [Theory]
    [InlineData(ServiceLaunchSettings.LowestPort)]
    [InlineData(ServiceLaunchSettings.HighestPort)]
    public void Parse_AtThePortBoundaries_Succeeds(int port)
    {
        var result = ServiceLaunchSettings.Parse(
            [$"--port={port}", "--myceliumUrl=https://localhost:7243"]);

        result.Should().NotBeNull();
        result!.Port.Should().Be(port);
    }

    [Fact]
    public void Parse_WithNoArgumentsAndNoConfiguration_ReturnsNull() =>
        ServiceLaunchSettings.Parse(Array.Empty<string>()).Should().BeNull();

    [Fact]
    public void Parse_WithNullArguments_ReturnsNull() =>
        ServiceLaunchSettings.Parse(arguments: null).Should().BeNull();

    [Fact]
    public void Parse_TakesTheFirstOccurrenceOfARepeatedFlag()
    {
        var result = ServiceLaunchSettings.Parse(
            ["--port=7111", "--port=8222", "--myceliumUrl=https://localhost:7243"]);

        result.Should().NotBeNull();
        result!.Port.Should().Be(7111);
    }

    [Fact]
    public void Parse_KeepsAValueContainingAnEqualsSign()
    {
        var result = ServiceLaunchSettings.Parse(
            ["--port=7111", "--myceliumUrl=https://localhost:7243", "--audience=a=b=c"]);

        result.Should().NotBeNull();
        result!.Audience.Should().Be("a=b=c");
    }

    [Fact]
    public void Parse_IgnoresUnrelatedArguments()
    {
        var result = ServiceLaunchSettings.Parse(
            ["--unrelated=value", "--port=7111", "positional", "--myceliumUrl=https://localhost:7243"]);

        result.Should().NotBeNull();
        result!.Port.Should().Be(7111);
    }

    [Fact]
    public void Parse_DoesNotMatchAFlagThatMerelyStartsTheSameWay()
    {
        var result = ServiceLaunchSettings.Parse(
            ["--portOffset=99", "--port=7111", "--myceliumUrl=https://localhost:7243"]);

        result.Should().NotBeNull();
        result!.Port.Should().Be(7111);
    }

    [Fact]
    public void Parse_WhenEveryRequiredSettingComesFromConfiguration_Succeeds()
    {
        var configuration = ConfigurationFrom(
            ("Port", "7100"), ("MyceliumUrl", "http://from-configuration"));

        var result = ServiceLaunchSettings.Parse(Array.Empty<string>(), configuration);

        result.Should().NotBeNull();
        result!.Port.Should().Be(7100);
        result.MyceliumUrl.Should().Be("http://from-configuration");
    }

    [Fact]
    public void Parse_WhenOptionalSettingsComeFromConfiguration_ReadsThemAll()
    {
        var configuration = ConfigurationFrom(
            ("Port", "5100"), ("MyceliumUrl", "http://mycelium"),
            ("Token", "configured-token"), ("SigningKey", "configured-key"),
            ("Issuer", "configured-issuer"), ("Audience", "configured-audience"));

        var result = ServiceLaunchSettings.Parse(Array.Empty<string>(), configuration);

        result.Should().NotBeNull();
        result!.Token.Should().Be("configured-token");
        result.SigningKey.Should().Be("configured-key");
        result.Issuer.Should().Be("configured-issuer");
        result.Audience.Should().Be("configured-audience");
    }

    [Fact]
    public void Parse_WhenAFlagAndConfigurationDisagree_TheFlagWins()
    {
        var configuration = ConfigurationFrom(
            ("Port", "1111"), ("MyceliumUrl", "http://configured-mycelium"));

        var result = ServiceLaunchSettings.Parse(
            ["--port=2222", "--myceliumUrl=http://flag-mycelium"], configuration);

        result.Should().NotBeNull();
        result!.Port.Should().Be(2222);
        result.MyceliumUrl.Should().Be("http://flag-mycelium");
    }

    [Fact]
    public void Parse_MixesFlagsAndConfiguration()
    {
        var configuration = ConfigurationFrom(("MyceliumUrl", "http://configured-mycelium"));

        var result = ServiceLaunchSettings.Parse(["--port=2222"], configuration);

        result.Should().NotBeNull();
        result!.Port.Should().Be(2222);
        result.MyceliumUrl.Should().Be("http://configured-mycelium");
    }

    [Fact]
    public void Parse_WithNullConfiguration_ReadsFlagsOnly() =>
        ServiceLaunchSettings.Parse(Array.Empty<string>(), configuration: null).Should().BeNull();

    // A flag name carrying an invisible character is a different flag. Culture-sensitive matching
    // treats it as the same one and then cuts the value by position, leaking part of the flag into it.
    [Theory]
    [InlineData(SoftHyphen)]
    [InlineData(ZeroWidthJoiner)]
    public void Parse_WhenThePortFlagCarriesAnInvisibleCharacter_DoesNotTreatItAsThePortFlag(string invisible) =>
        ServiceLaunchSettings.Parse(
            [$"--po{invisible}rt=7111", "--myceliumUrl=https://localhost:7243"]).Should().BeNull();

    [Theory]
    [InlineData(SoftHyphen)]
    [InlineData(ZeroWidthJoiner)]
    public void Parse_WhenTheUrlFlagCarriesAnInvisibleCharacter_DoesNotYieldADamagedUrl(string invisible) =>
        ServiceLaunchSettings.Parse(
            ["--port=7111", $"--myceliumU{invisible}rl=https://localhost:7243"]).Should().BeNull();

    [Fact]
    public void UsageMessage_NamesEveryCommonFlag()
    {
        var usage = ServiceLaunchSettings.UsageMessage;

        usage.Should().Contain("--port")
            .And.Contain("--myceliumUrl")
            .And.Contain("--issuer")
            .And.Contain("--audience");
    }

    [Fact]
    public void UsageMessage_NamesTheCredentialsAsConfigurationSettings_NotFlags()
    {
        var usage = ServiceLaunchSettings.UsageMessage;

        usage.Should().NotContain("--token").And.NotContain("--signingKey");
        usage.Should().Contain("Token").And.Contain("SigningKey");
    }

    [Fact]
    public void BuildUsageMessage_PlacesServiceFlagsAlongsideTheCommonOnes()
    {
        var usage = ServiceLaunchSettings.BuildUsageMessage(
            " --mode=<consumes|produces>",
            "\n  --mode         Whether the service consumes or produces");

        usage.Should().Contain("--port").And.Contain("--mode=<consumes|produces>");
        usage.Should().Contain("Whether the service consumes or produces");
    }
}
