using vos.ManagedMicroservice.EndpointCaller.Configuration;
using FluentAssertions;
using Xunit;

namespace vos.ManagedMicroservice.EndpointCaller.Tests;

public class CliArgsTests
{
    [Fact]
    public void Parse_WithRequiredArgs_ReturnsParsedArgs()
    {
        var args = new[] { "--port=7112", "--brokerUrl=https://localhost:7243" };

        var result = CliArgs.Parse(args);

        result.Should().NotBeNull();
        result!.Port.Should().Be(7112);
        result.BrokerUrl.Should().Be("https://localhost:7243");
    }

    [Fact]
    public void Parse_MissingRequiredArg_ReturnsNull()
    {
        var args = new[] { "--port=7112" };

        var result = CliArgs.Parse(args);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("--port=0")]
    [InlineData("--port=65536")]
    [InlineData("--port=NaN")]
    public void Parse_InvalidPort_ReturnsNull(string portArg)
    {
        var args = new[] { portArg, "--brokerUrl=https://localhost:7243" };

        var result = CliArgs.Parse(args);

        result.Should().BeNull();
    }
}
