using Xunit;

namespace vos.Taproot.Tests;

[Collection(nameof(CliEnvVarCollection))]
public class ConsoleOptionsTests
{
    // ========== Parse() Tests ==========

    [Fact]
    public void Parse_WithNoArgs_ReturnsDefaultMyceliumUrl()
    {
        var options = ConsoleOptions.Parse(Array.Empty<string>());

        Assert.Equal("https://localhost:7243", options.MyceliumUrl);
    }

    [Fact]
    public void Parse_WithMyceliumUrlArg_SetsMyceliumUrl()
    {
        var options = ConsoleOptions.Parse(new[] { "--mycelium-url=https://example.com:8080" });

        Assert.Equal("https://example.com:8080", options.MyceliumUrl);
    }

    [Fact]
    public void Parse_WithMyceliumShortArg_SetsMyceliumUrl()
    {
        var options = ConsoleOptions.Parse(new[] { "--mycelium=https://example.com:9090" });

        Assert.Equal("https://example.com:9090", options.MyceliumUrl);
    }

    [Fact]
    public void Parse_WithMyceliumUrlArg_CaseInsensitive()
    {
        var options = ConsoleOptions.Parse(new[] { "--MYCELIUM-URL=https://example.com" });

        Assert.Equal("https://example.com", options.MyceliumUrl);
    }

    [Fact]
    public void Parse_WithMultipleArgs_UsesLastMyceliumUrl()
    {
        var options = ConsoleOptions.Parse(new[] {
            "--mycelium-url=https://first.com",
            "--mycelium=https://second.com"
        });

        Assert.Equal("https://second.com", options.MyceliumUrl);
    }

    [Fact]
    public void Parse_WithUnrelatedArgs_IgnoresThem()
    {
        var options = ConsoleOptions.Parse(new[] {
            "--verbose",
            "--output=file.txt",
            "--mycelium-url=https://example.com"
        });

        Assert.Equal("https://example.com", options.MyceliumUrl);
    }

    [Fact]
    public void Parse_WithEmptyMyceliumUrl_SetsEmptyString()
    {
        var options = ConsoleOptions.Parse(new[] { "--mycelium-url=" });

        Assert.Equal("", options.MyceliumUrl);
    }

    [Fact]
    public void Parse_WithMalformedArg_IgnoresIt()
    {
        var options = ConsoleOptions.Parse(new[] { "--mycelium-url" }); // Missing =

        Assert.Equal("https://localhost:7243", options.MyceliumUrl);
    }

    [Fact]
    public void Parse_WithPartialMatch_IgnoresIt()
    {
        var options = ConsoleOptions.Parse(new[] { "--mycelium-urls=https://example.com" }); // Extra 's'

        Assert.Equal("https://localhost:7243", options.MyceliumUrl);
    }

    // ========== Environment Variable Tests ==========

    [Fact]
    public void Parse_WithEnvVariable_UseEnvValue()
    {
        var originalValue = Environment.GetEnvironmentVariable("VOS_MYCELIUM_URL");
        try
        {
            Environment.SetEnvironmentVariable("VOS_MYCELIUM_URL", "https://env.example.com");

            var options = ConsoleOptions.Parse(Array.Empty<string>());

            Assert.Equal("https://env.example.com", options.MyceliumUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VOS_MYCELIUM_URL", originalValue);
        }
    }

    [Fact]
    public void Parse_CommandLineOverridesEnvVariable()
    {
        var originalValue = Environment.GetEnvironmentVariable("VOS_MYCELIUM_URL");
        try
        {
            Environment.SetEnvironmentVariable("VOS_MYCELIUM_URL", "https://env.example.com");

            var options = ConsoleOptions.Parse(new[] { "--mycelium-url=https://cli.example.com" });

            Assert.Equal("https://cli.example.com", options.MyceliumUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VOS_MYCELIUM_URL", originalValue);
        }
    }

    [Fact]
    public void Parse_WithEmptyEnvVariable_UsesDefault()
    {
        var originalValue = Environment.GetEnvironmentVariable("VOS_MYCELIUM_URL");
        try
        {
            Environment.SetEnvironmentVariable("VOS_MYCELIUM_URL", "");

            var options = ConsoleOptions.Parse(Array.Empty<string>());

            Assert.Equal("https://localhost:7243", options.MyceliumUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VOS_MYCELIUM_URL", originalValue);
        }
    }

    [Fact]
    public void Parse_WithWhitespaceEnvVariable_UsesDefault()
    {
        var originalValue = Environment.GetEnvironmentVariable("VOS_MYCELIUM_URL");
        try
        {
            Environment.SetEnvironmentVariable("VOS_MYCELIUM_URL", "   ");

            var options = ConsoleOptions.Parse(Array.Empty<string>());

            Assert.Equal("https://localhost:7243", options.MyceliumUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VOS_MYCELIUM_URL", originalValue);
        }
    }

    // ========== Env-var precedence ==========

    [Fact]
    public void Parse_VosMyceliumUrlEnvVar_OverridesDefault()
    {
        var original = Environment.GetEnvironmentVariable("VOS_MYCELIUM_URL");
        try
        {
            Environment.SetEnvironmentVariable("VOS_MYCELIUM_URL", "https://custom-mycelium:9000");
            var options = ConsoleOptions.Parse(Array.Empty<string>());
            Assert.Equal("https://custom-mycelium:9000", options.MyceliumUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VOS_MYCELIUM_URL", original);
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
        var origUrl = Environment.GetEnvironmentVariable("VOS_MYCELIUM_URL");
        var origKey = Environment.GetEnvironmentVariable("VOS_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("VOS_MYCELIUM_URL", null);
            Environment.SetEnvironmentVariable("VOS_API_KEY", null);
            var options = ConsoleOptions.Parse(Array.Empty<string>());
            Assert.Equal("https://localhost:7243", options.MyceliumUrl);
            Assert.Null(options.ApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VOS_MYCELIUM_URL", origUrl);
            Environment.SetEnvironmentVariable("VOS_API_KEY", origKey);
        }
    }

    [Fact]
    public void Parse_CommandLineArgs_OverrideEnvVars()
    {
        var origUrl = Environment.GetEnvironmentVariable("VOS_MYCELIUM_URL");
        try
        {
            Environment.SetEnvironmentVariable("VOS_MYCELIUM_URL", "https://from-env:9000");
            var options = ConsoleOptions.Parse(new[] { "--mycelium-url=https://from-cli:8000", "--api-key=clikey" });
            Assert.Equal("https://from-cli:8000", options.MyceliumUrl);
            Assert.Equal("clikey", options.ApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VOS_MYCELIUM_URL", origUrl);
        }
    }
}
