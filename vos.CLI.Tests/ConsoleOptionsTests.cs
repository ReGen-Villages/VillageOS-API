using Xunit;

namespace vos.CLI.Tests;

[Collection(nameof(CliEnvVarCollection))]
public class ConsoleOptionsTests
{
    // ========== Parse() Tests ==========

    [Fact]
    public void Parse_WithNoArgs_ReturnsDefaultBrokerUrl()
    {
        var options = ConsoleOptions.Parse(Array.Empty<string>());

        Assert.Equal("https://localhost:7243", options.BrokerUrl);
    }

    [Fact]
    public void Parse_WithBrokerUrlArg_SetsBrokerUrl()
    {
        var options = ConsoleOptions.Parse(new[] { "--broker-url=https://example.com:8080" });

        Assert.Equal("https://example.com:8080", options.BrokerUrl);
    }

    [Fact]
    public void Parse_WithBrokerShortArg_SetsBrokerUrl()
    {
        var options = ConsoleOptions.Parse(new[] { "--broker=https://example.com:9090" });

        Assert.Equal("https://example.com:9090", options.BrokerUrl);
    }

    [Fact]
    public void Parse_WithBrokerUrlArg_CaseInsensitive()
    {
        var options = ConsoleOptions.Parse(new[] { "--BROKER-URL=https://example.com" });

        Assert.Equal("https://example.com", options.BrokerUrl);
    }

    [Fact]
    public void Parse_WithMultipleArgs_UsesLastBrokerUrl()
    {
        var options = ConsoleOptions.Parse(new[] {
            "--broker-url=https://first.com",
            "--broker=https://second.com"
        });

        Assert.Equal("https://second.com", options.BrokerUrl);
    }

    [Fact]
    public void Parse_WithUnrelatedArgs_IgnoresThem()
    {
        var options = ConsoleOptions.Parse(new[] {
            "--verbose",
            "--output=file.txt",
            "--broker-url=https://example.com"
        });

        Assert.Equal("https://example.com", options.BrokerUrl);
    }

    [Fact]
    public void Parse_WithEmptyBrokerUrl_SetsEmptyString()
    {
        var options = ConsoleOptions.Parse(new[] { "--broker-url=" });

        Assert.Equal("", options.BrokerUrl);
    }

    [Fact]
    public void Parse_WithMalformedArg_IgnoresIt()
    {
        var options = ConsoleOptions.Parse(new[] { "--broker-url" }); // Missing =

        Assert.Equal("https://localhost:7243", options.BrokerUrl);
    }

    [Fact]
    public void Parse_WithPartialMatch_IgnoresIt()
    {
        var options = ConsoleOptions.Parse(new[] { "--broker-urls=https://example.com" }); // Extra 's'

        Assert.Equal("https://localhost:7243", options.BrokerUrl);
    }

    // ========== Environment Variable Tests ==========

    [Fact]
    public void Parse_WithEnvVariable_UseEnvValue()
    {
        var originalValue = Environment.GetEnvironmentVariable("VOS_BROKER_URL");
        try
        {
            Environment.SetEnvironmentVariable("VOS_BROKER_URL", "https://env.example.com");

            var options = ConsoleOptions.Parse(Array.Empty<string>());

            Assert.Equal("https://env.example.com", options.BrokerUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VOS_BROKER_URL", originalValue);
        }
    }

    [Fact]
    public void Parse_CommandLineOverridesEnvVariable()
    {
        var originalValue = Environment.GetEnvironmentVariable("VOS_BROKER_URL");
        try
        {
            Environment.SetEnvironmentVariable("VOS_BROKER_URL", "https://env.example.com");

            var options = ConsoleOptions.Parse(new[] { "--broker-url=https://cli.example.com" });

            Assert.Equal("https://cli.example.com", options.BrokerUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VOS_BROKER_URL", originalValue);
        }
    }

    [Fact]
    public void Parse_WithEmptyEnvVariable_UsesDefault()
    {
        var originalValue = Environment.GetEnvironmentVariable("VOS_BROKER_URL");
        try
        {
            Environment.SetEnvironmentVariable("VOS_BROKER_URL", "");

            var options = ConsoleOptions.Parse(Array.Empty<string>());

            Assert.Equal("https://localhost:7243", options.BrokerUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VOS_BROKER_URL", originalValue);
        }
    }

    [Fact]
    public void Parse_WithWhitespaceEnvVariable_UsesDefault()
    {
        var originalValue = Environment.GetEnvironmentVariable("VOS_BROKER_URL");
        try
        {
            Environment.SetEnvironmentVariable("VOS_BROKER_URL", "   ");

            var options = ConsoleOptions.Parse(Array.Empty<string>());

            Assert.Equal("https://localhost:7243", options.BrokerUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VOS_BROKER_URL", originalValue);
        }
    }

    // ========== Env-var precedence (moved from CoverageGapTests under Task #5437) ==========

    [Fact]
    public void Parse_VosBrokerUrlEnvVar_OverridesDefault()
    {
        var original = Environment.GetEnvironmentVariable("VOS_BROKER_URL");
        try
        {
            Environment.SetEnvironmentVariable("VOS_BROKER_URL", "https://custom-broker:9000");
            var options = ConsoleOptions.Parse(Array.Empty<string>());
            Assert.Equal("https://custom-broker:9000", options.BrokerUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VOS_BROKER_URL", original);
        }
    }

    [Fact]
    public void Parse_VosApiKeyEnvVar_PopulatesApiKey()
    {
        var original = Environment.GetEnvironmentVariable("VOS_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("VOS_API_KEY", "test-api-key-from-env");
            var options = ConsoleOptions.Parse(Array.Empty<string>());
            Assert.Equal("test-api-key-from-env", options.ApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VOS_API_KEY", original);
        }
    }

    [Fact]
    public void Parse_DefaultsApplyWhenNeitherEnvNorArgs()
    {
        var origUrl = Environment.GetEnvironmentVariable("VOS_BROKER_URL");
        var origKey = Environment.GetEnvironmentVariable("VOS_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("VOS_BROKER_URL", null);
            Environment.SetEnvironmentVariable("VOS_API_KEY", null);
            var options = ConsoleOptions.Parse(Array.Empty<string>());
            Assert.Equal("https://localhost:7243", options.BrokerUrl);
            Assert.Null(options.ApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VOS_BROKER_URL", origUrl);
            Environment.SetEnvironmentVariable("VOS_API_KEY", origKey);
        }
    }

    [Fact]
    public void Parse_CommandLineArgs_OverrideEnvVars()
    {
        var origUrl = Environment.GetEnvironmentVariable("VOS_BROKER_URL");
        try
        {
            Environment.SetEnvironmentVariable("VOS_BROKER_URL", "https://from-env:9000");
            var options = ConsoleOptions.Parse(new[] { "--broker-url=https://from-cli:8000", "--api-key=clikey" });
            Assert.Equal("https://from-cli:8000", options.BrokerUrl);
            Assert.Equal("clikey", options.ApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VOS_BROKER_URL", origUrl);
        }
    }
}
