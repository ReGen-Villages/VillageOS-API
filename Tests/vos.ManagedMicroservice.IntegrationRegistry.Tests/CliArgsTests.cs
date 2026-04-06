using vos.ManagedMicroservice.IntegrationRegistry.Configuration;
using FluentAssertions;
using Xunit;

namespace vos.ManagedMicroservice.IntegrationRegistry.Tests;

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

        var result = CliArgs.Parse(args);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("--port=0")]
    [InlineData("--port=70000")]
    [InlineData("--port=bad")]
    public void Parse_WithInvalidPort_ReturnsNull(string portArg)
    {
        var args = new[] { portArg, "--brokerUrl=https://localhost:7243" };

        var result = CliArgs.Parse(args);

        result.Should().BeNull();
    }
}
