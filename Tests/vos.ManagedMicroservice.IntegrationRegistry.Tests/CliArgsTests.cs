using vos.ManagedMicroservice.IntegrationRegistry.Configuration;
using FluentAssertions;
using Xunit;

namespace vos.ManagedMicroservice.IntegrationRegistry.Tests;

// CliArgs.Parse now falls back to INTEGRATIONREGISTRY_* env vars (so WebApplicationFactory<Program>
// tests can inject config) — these tests run with a scrubbed env so they exercise the
// pure args-only path. xUnit constructs a fresh instance per [Fact], so the ctor runs before
// every test. The [Collection] groups env-var-touching tests so they don't race with the
// WebApplicationFactory-based tests that set the same vars in InitializeAsync.
[Collection(nameof(IntegrationRegistryEnvVarCollection))]
public class CliArgsTests : IDisposable
{
    private readonly Dictionary<string, string?> _saved;
    private static readonly string[] ScrubbedVars =
    {
        "INTEGRATIONREGISTRY_PORT", "INTEGRATIONREGISTRY_BROKER_URL",
        "INTEGRATIONREGISTRY_TOKEN", "INTEGRATIONREGISTRY_SIGNING_KEY",
        "INTEGRATIONREGISTRY_ISSUER", "INTEGRATIONREGISTRY_AUDIENCE"
    };

    public CliArgsTests()
    {
        _saved = ScrubbedVars.ToDictionary(v => v, v => Environment.GetEnvironmentVariable(v));
        foreach (var v in ScrubbedVars)
            Environment.SetEnvironmentVariable(v, null);
    }

    public void Dispose()
    {
        foreach (var (key, value) in _saved)
            Environment.SetEnvironmentVariable(key, value);
    }

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

    // ---- Env-var fallback (added for WebApplicationFactory<Program> tests) ----

    [Fact]
    public void Parse_NoArgs_AllRequiredFromEnvVars_ReturnsCliArgs()
    {
        Environment.SetEnvironmentVariable("INTEGRATIONREGISTRY_PORT", "7100");
        Environment.SetEnvironmentVariable("INTEGRATIONREGISTRY_BROKER_URL", "http://from-env");

        var result = CliArgs.Parse(Array.Empty<string>());

        result.Should().NotBeNull();
        result!.Port.Should().Be(7100);
        result.BrokerUrl.Should().Be("http://from-env");
    }

    [Fact]
    public void Parse_ArgsTakePrecedenceOverEnvVars()
    {
        Environment.SetEnvironmentVariable("INTEGRATIONREGISTRY_PORT", "1111");
        Environment.SetEnvironmentVariable("INTEGRATIONREGISTRY_BROKER_URL", "http://env-broker");
        var args = new[] { "--port=2222", "--brokerUrl=http://cli-broker" };

        var result = CliArgs.Parse(args);

        result.Should().NotBeNull();
        result!.Port.Should().Be(2222);
        result.BrokerUrl.Should().Be("http://cli-broker");
    }

    [Fact]
    public void Parse_EnvVarsCoverOptionalFlagsToo()
    {
        Environment.SetEnvironmentVariable("INTEGRATIONREGISTRY_PORT", "5100");
        Environment.SetEnvironmentVariable("INTEGRATIONREGISTRY_BROKER_URL", "http://broker");
        Environment.SetEnvironmentVariable("INTEGRATIONREGISTRY_TOKEN", "env-token");
        Environment.SetEnvironmentVariable("INTEGRATIONREGISTRY_SIGNING_KEY", "env-key");
        Environment.SetEnvironmentVariable("INTEGRATIONREGISTRY_ISSUER", "env-issuer");
        Environment.SetEnvironmentVariable("INTEGRATIONREGISTRY_AUDIENCE", "env-audience");

        var result = CliArgs.Parse(Array.Empty<string>());

        result.Should().NotBeNull();
        result!.Token.Should().Be("env-token");
        result.SigningKey.Should().Be("env-key");
        result.Issuer.Should().Be("env-issuer");
        result.Audience.Should().Be("env-audience");
    }

    [Fact]
    public void Parse_NoArgsNoEnvVars_ReturnsNull()
    {
        CliArgs.Parse(Array.Empty<string>()).Should().BeNull();
    }
}
