using vos.ManagedMicroservice.Metabolism.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace vos.ManagedMicroservice.Metabolism.Tests;

public class CliArgsTests
{
    [Fact]
    public void Parse_AllArgsPresent_ReturnsCliArgs()
    {
        var args = new[] { "--port=5100", "--brokerUrl=http://localhost:5000", "--mode=consumes" };
        var result = CliArgs.Parse(args);

        result.Should().NotBeNull();
        result!.Port.Should().Be(5100);
        result.BrokerUrl.Should().Be("http://localhost:5000");
        result.Mode.Should().Be("consumes");
    }

    [Fact]
    public void Parse_ProducesMode_Accepted()
    {
        var args = new[] { "--port=5200", "--brokerUrl=http://broker", "--mode=produces" };
        var result = CliArgs.Parse(args);

        result.Should().NotBeNull();
        result!.Mode.Should().Be("produces");
    }

    [Fact]
    public void Parse_MixedCaseMode_NormalizedToLower()
    {
        var args = new[] { "--port=5100", "--brokerUrl=http://broker", "--mode=CONSUMES" };
        var result = CliArgs.Parse(args);

        result.Should().NotBeNull();
        result!.Mode.Should().Be("consumes");
    }

    [Fact]
    public void Parse_MissingPort_ReturnsNull()
    {
        var args = new[] { "--brokerUrl=http://broker", "--mode=consumes" };
        CliArgs.Parse(args).Should().BeNull();
    }

    [Fact]
    public void Parse_MissingBrokerUrl_ReturnsNull()
    {
        var args = new[] { "--port=5100", "--mode=consumes" };
        CliArgs.Parse(args).Should().BeNull();
    }

    [Fact]
    public void Parse_MissingMode_ReturnsNull()
    {
        var args = new[] { "--port=5100", "--brokerUrl=http://broker" };
        CliArgs.Parse(args).Should().BeNull();
    }

    [Fact]
    public void Parse_InvalidMode_ReturnsNull()
    {
        var args = new[] { "--port=5100", "--brokerUrl=http://broker", "--mode=invalid" };
        CliArgs.Parse(args).Should().BeNull();
    }

    [Fact]
    public void Parse_InvalidPort_ReturnsNull()
    {
        var args = new[] { "--port=abc", "--brokerUrl=http://broker", "--mode=consumes" };
        CliArgs.Parse(args).Should().BeNull();
    }

    [Fact]
    public void Parse_EmptyArgs_ReturnsNull()
    {
        CliArgs.Parse(Array.Empty<string>()).Should().BeNull();
    }

    [Fact]
    public void Parse_ArgsInAnyOrder_Works()
    {
        var args = new[] { "--mode=produces", "--port=9999", "--brokerUrl=http://example.com" };
        var result = CliArgs.Parse(args);

        result.Should().NotBeNull();
        result!.Port.Should().Be(9999);
        result.Mode.Should().Be("produces");
    }

    [Fact]
    public void Parse_WithTokenAndSigningKey_ParsesBoth()
    {
        var args = new[]
        {
            "--port=5100", "--brokerUrl=http://broker", "--mode=consumes",
            "--token=my.jwt.token", "--signingKey=c29tZWtleQ=="
        };
        var result = CliArgs.Parse(args);

        result.Should().NotBeNull();
        result!.Token.Should().Be("my.jwt.token");
        result.SigningKey.Should().Be("c29tZWtleQ==");
    }

    [Fact]
    public void Parse_WithoutTokenAndSigningKey_DefaultsToNull()
    {
        var args = new[] { "--port=5100", "--brokerUrl=http://broker", "--mode=consumes" };
        var result = CliArgs.Parse(args);

        result.Should().NotBeNull();
        result!.Token.Should().BeNull();
        result.SigningKey.Should().BeNull();
    }

    [Fact]
    public void Parse_WithTokenOnly_SigningKeyIsNull()
    {
        var args = new[] { "--port=5100", "--brokerUrl=http://broker", "--mode=consumes", "--token=jwt" };
        var result = CliArgs.Parse(args);

        result.Should().NotBeNull();
        result!.Token.Should().Be("jwt");
        result.SigningKey.Should().BeNull();
    }

    [Fact]
    public void Parse_WithIssuerAndAudience_ParsesBoth()
    {
        // Bug #5391 — broker passes its JWT issuer + audience via CLI so the
        // daemon validates incoming /handle requests against exactly the
        // values the broker signed with. The broker-side change is Bug #5390.
        var args = new[]
        {
            "--port=5100", "--brokerUrl=http://broker", "--mode=consumes",
            "--issuer=VillageOS", "--audience=VillageOSClients"
        };
        var result = CliArgs.Parse(args);

        result.Should().NotBeNull();
        result!.Issuer.Should().Be("VillageOS");
        result.Audience.Should().Be("VillageOSClients");
    }

    [Fact]
    public void Parse_WithoutIssuerAndAudience_DefaultsToNull()
    {
        var args = new[] { "--port=5100", "--brokerUrl=http://broker", "--mode=consumes" };
        var result = CliArgs.Parse(args);

        result.Should().NotBeNull();
        result!.Issuer.Should().BeNull(
            "the daemon must fall back to a hardcoded default only when the broker did not specify");
        result.Audience.Should().BeNull();
    }

    // ---- IConfiguration fallback ----

    private static IConfiguration ConfigFrom(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value))).Build();

    [Fact]
    public void Parse_NoArgs_AllRequiredFromConfig_ReturnsCliArgs()
    {
        var config = ConfigFrom(
            ("Port", "7100"), ("BrokerUrl", "http://from-config"), ("Mode", "produces"));

        var result = CliArgs.Parse(Array.Empty<string>(), config);

        result.Should().NotBeNull();
        result!.Port.Should().Be(7100);
        result.BrokerUrl.Should().Be("http://from-config");
        result.Mode.Should().Be("produces");
    }

    [Fact]
    public void Parse_ArgsTakePrecedenceOverConfig()
    {
        var config = ConfigFrom(
            ("Port", "1111"), ("BrokerUrl", "http://config-broker"), ("Mode", "produces"));
        var args = new[] { "--port=2222", "--brokerUrl=http://cli-broker", "--mode=consumes" };

        var result = CliArgs.Parse(args, config);

        result.Should().NotBeNull();
        result!.Port.Should().Be(2222);
        result.BrokerUrl.Should().Be("http://cli-broker");
        result.Mode.Should().Be("consumes");
    }

    [Fact]
    public void Parse_ConfigCoversOptionalFlagsToo()
    {
        var config = ConfigFrom(
            ("Port", "5100"), ("BrokerUrl", "http://broker"), ("Mode", "consumes"),
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
        // Empty args + null config → still null (no fallback at all).
        CliArgs.Parse(Array.Empty<string>(), config: null).Should().BeNull();
    }
}
