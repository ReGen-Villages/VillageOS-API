using vos.Service.Metabolism.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace vos.Service.Metabolism.Tests;

public class CliArgsTests
{
    [Fact]
    public void Parse_AllArgsPresent_ReturnsCliArgs()
    {
        var args = new[] { "--port=5100", "--myceliumUrl=http://localhost:5000", "--mode=consumes" };
        var result = CliArgs.Parse(args);

        result.Should().NotBeNull();
        result!.Port.Should().Be(5100);
        result.MyceliumUrl.Should().Be("http://localhost:5000");
        result.Mode.Should().Be("consumes");
    }

    [Fact]
    public void Parse_ProducesMode_Accepted()
    {
        var args = new[] { "--port=5200", "--myceliumUrl=http://mycelium", "--mode=produces" };
        var result = CliArgs.Parse(args);

        result.Should().NotBeNull();
        result!.Mode.Should().Be("produces");
    }

    [Fact]
    public void Parse_MixedCaseMode_NormalizedToLower()
    {
        var args = new[] { "--port=5100", "--myceliumUrl=http://mycelium", "--mode=CONSUMES" };
        var result = CliArgs.Parse(args);

        result.Should().NotBeNull();
        result!.Mode.Should().Be("consumes");
    }

    [Fact]
    public void Parse_MissingPort_ReturnsNull()
    {
        var args = new[] { "--myceliumUrl=http://mycelium", "--mode=consumes" };
        CliArgs.Parse(args).Should().BeNull();
    }

    [Fact]
    public void Parse_MissingMyceliumUrl_ReturnsNull()
    {
        var args = new[] { "--port=5100", "--mode=consumes" };
        CliArgs.Parse(args).Should().BeNull();
    }

    [Fact]
    public void Parse_MissingMode_ReturnsNull()
    {
        var args = new[] { "--port=5100", "--myceliumUrl=http://mycelium" };
        CliArgs.Parse(args).Should().BeNull();
    }

    [Fact]
    public void Parse_InvalidMode_ReturnsNull()
    {
        var args = new[] { "--port=5100", "--myceliumUrl=http://mycelium", "--mode=invalid" };
        CliArgs.Parse(args).Should().BeNull();
    }

    [Fact]
    public void Parse_InvalidPort_ReturnsNull()
    {
        var args = new[] { "--port=abc", "--myceliumUrl=http://mycelium", "--mode=consumes" };
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
        var args = new[] { "--mode=produces", "--port=9999", "--myceliumUrl=http://example.com" };
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
            "--port=5100", "--myceliumUrl=http://mycelium", "--mode=consumes",
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
        var args = new[] { "--port=5100", "--myceliumUrl=http://mycelium", "--mode=consumes" };
        var result = CliArgs.Parse(args);

        result.Should().NotBeNull();
        result!.Token.Should().BeNull();
        result.SigningKey.Should().BeNull();
    }

    [Fact]
    public void Parse_WithTokenOnly_SigningKeyIsNull()
    {
        var args = new[] { "--port=5100", "--myceliumUrl=http://mycelium", "--mode=consumes", "--token=jwt" };
        var result = CliArgs.Parse(args);

        result.Should().NotBeNull();
        result!.Token.Should().Be("jwt");
        result.SigningKey.Should().BeNull();
    }

    [Fact]
    public void Parse_WithIssuerAndAudience_ParsesBoth()
    {
        // Bug #5391 — mycelium passes its JWT issuer + audience via CLI so the
        // daemon validates incoming /handle requests against exactly the
        // values Mycelium signed with. The mycelium-side change is Bug #5390.
        var args = new[]
        {
            "--port=5100", "--myceliumUrl=http://mycelium", "--mode=consumes",
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
        var args = new[] { "--port=5100", "--myceliumUrl=http://mycelium", "--mode=consumes" };
        var result = CliArgs.Parse(args);

        result.Should().NotBeNull();
        result!.Issuer.Should().BeNull(
            "the daemon must fall back to a hardcoded default only when Mycelium did not specify");
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
            ("Port", "7100"), ("MyceliumUrl", "http://from-config"), ("Mode", "produces"));

        var result = CliArgs.Parse(Array.Empty<string>(), config);

        result.Should().NotBeNull();
        result!.Port.Should().Be(7100);
        result.MyceliumUrl.Should().Be("http://from-config");
        result.Mode.Should().Be("produces");
    }

    [Fact]
    public void Parse_ArgsTakePrecedenceOverConfig()
    {
        var config = ConfigFrom(
            ("Port", "1111"), ("MyceliumUrl", "http://config-mycelium"), ("Mode", "produces"));
        var args = new[] { "--port=2222", "--myceliumUrl=http://cli-mycelium", "--mode=consumes" };

        var result = CliArgs.Parse(args, config);

        result.Should().NotBeNull();
        result!.Port.Should().Be(2222);
        result.MyceliumUrl.Should().Be("http://cli-mycelium");
        result.Mode.Should().Be("consumes");
    }

    [Fact]
    public void Parse_ConfigCoversOptionalFlagsToo()
    {
        var config = ConfigFrom(
            ("Port", "5100"), ("MyceliumUrl", "http://mycelium"), ("Mode", "consumes"),
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
