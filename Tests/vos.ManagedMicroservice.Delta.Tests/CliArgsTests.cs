using vos.ManagedMicroservice.Delta.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace vos.ManagedMicroservice.Delta.Tests;

public class CliArgsTests
{
    [Fact]
    public void Parse_WithRequiredArgs_ReturnsParsedArgs()
    {
        var args = new[] { "--port=7111", "--brokerUrl=https://localhost:7243" };

        var result = CliArgs.Parse(args);

        result.Should().NotBeNull();
        result!.Port.Should().Be(7111);
        result.BrokerUrl.Should().Be("https://localhost:7243");
    }

    [Fact]
    public void Parse_WhenBrokerUrlMissing_ReturnsNull()
    {
        var args = new[] { "--port=7111" };

        CliArgs.Parse(args).Should().BeNull();
    }

    [Fact]
    public void Parse_WhenPortMissing_ReturnsNull()
    {
        var args = new[] { "--brokerUrl=https://localhost:7243" };

        CliArgs.Parse(args).Should().BeNull();
    }

    [Theory]
    [InlineData("--port=0")]
    [InlineData("--port=70000")]
    [InlineData("--port=bad")]
    public void Parse_WithInvalidPort_ReturnsNull(string portArg)
    {
        var args = new[] { portArg, "--brokerUrl=https://localhost:7243" };

        CliArgs.Parse(args).Should().BeNull();
    }

    [Fact]
    public void Parse_WithTokenAndSigningKey_ParsesBoth()
    {
        var args = new[]
        {
            "--port=7111", "--brokerUrl=https://localhost:7243",
            "--token=my.jwt.token", "--signingKey=c29tZWtleQ=="
        };

        var result = CliArgs.Parse(args);

        result.Should().NotBeNull();
        result!.Token.Should().Be("my.jwt.token");
        result.SigningKey.Should().Be("c29tZWtleQ==");
    }

    [Fact]
    public void Parse_WithoutOptionals_DefaultsToNull()
    {
        var args = new[] { "--port=7111", "--brokerUrl=https://localhost:7243" };

        var result = CliArgs.Parse(args);

        result.Should().NotBeNull();
        result!.Token.Should().BeNull();
        result.SigningKey.Should().BeNull();
        result.Issuer.Should().BeNull();
        result.Audience.Should().BeNull();
    }

    [Fact]
    public void Parse_WithIssuerAndAudience_ParsesBoth()
    {
        // Bug #5391 — broker passes its JWT issuer + audience via CLI so the
        // daemon validates incoming requests against exactly the values the
        // broker signed with.
        var args = new[]
        {
            "--port=7111", "--brokerUrl=https://localhost:7243",
            "--issuer=VillageOS", "--audience=VillageOSClients"
        };

        var result = CliArgs.Parse(args);

        result.Should().NotBeNull();
        result!.Issuer.Should().Be("VillageOS");
        result.Audience.Should().Be("VillageOSClients");
    }

    // ---- IConfiguration fallback ----

    private static IConfiguration ConfigFrom(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value))).Build();

    [Fact]
    public void Parse_NoArgs_AllRequiredFromConfig_ReturnsCliArgs()
    {
        var config = ConfigFrom(("Port", "7100"), ("BrokerUrl", "http://from-config"));

        var result = CliArgs.Parse(Array.Empty<string>(), config);

        result.Should().NotBeNull();
        result!.Port.Should().Be(7100);
        result.BrokerUrl.Should().Be("http://from-config");
    }

    [Fact]
    public void Parse_ArgsTakePrecedenceOverConfig()
    {
        var config = ConfigFrom(("Port", "1111"), ("BrokerUrl", "http://config-broker"));
        var args = new[] { "--port=2222", "--brokerUrl=http://cli-broker" };

        var result = CliArgs.Parse(args, config);

        result.Should().NotBeNull();
        result!.Port.Should().Be(2222);
        result.BrokerUrl.Should().Be("http://cli-broker");
    }

    [Fact]
    public void Parse_ConfigCoversOptionalFlagsToo()
    {
        var config = ConfigFrom(
            ("Port", "5100"), ("BrokerUrl", "http://broker"),
            ("Token", "cfg-token"), ("SigningKey", "cfg-key"),
            ("Issuer", "cfg-issuer"), ("Audience", "cfg-audience"));

        var result = CliArgs.Parse(Array.Empty<string>(), config);

        result.Should().NotBeNull();
        result!.Token.Should().Be("cfg-token");
        result.SigningKey.Should().Be("cfg-key");
        result.Issuer.Should().Be("cfg-issuer");
        result.Audience.Should().Be("cfg-audience");
    }

    [Fact]
    public void Parse_NullConfig_BehavesAsArgsOnly()
    {
        CliArgs.Parse(Array.Empty<string>(), config: null).Should().BeNull();
    }
}
