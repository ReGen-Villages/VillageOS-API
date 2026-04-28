using vos.ManagedMicroservice.Metabolism.Configuration;
using FluentAssertions;
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
}
