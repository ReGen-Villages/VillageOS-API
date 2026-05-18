using vos.ManagedMicroservice.Metabolism.Configuration;
using FluentAssertions;
using Xunit;

namespace vos.ManagedMicroservice.Metabolism.Tests;

// CliArgs.Parse now falls back to METABOLISM_* env vars (so WebApplicationFactory<Program>
// tests can inject config) — these tests run with a scrubbed env so they exercise the
// pure args-only path. xUnit constructs a fresh instance per [Fact], so the ctor runs
// before every test. The [Collection] groups env-var-touching tests so they don't race.
[Collection(nameof(MetabolismEnvVarCollection))]
public class CliArgsTests : IDisposable
{
    private readonly Dictionary<string, string?> _saved;
    private static readonly string[] ScrubbedVars =
    {
        "METABOLISM_PORT", "METABOLISM_BROKER_URL", "METABOLISM_MODE",
        "METABOLISM_TOKEN", "METABOLISM_SIGNING_KEY", "METABOLISM_ISSUER", "METABOLISM_AUDIENCE"
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

    // ---- Env-var fallback (added for WebApplicationFactory<Program> tests) ----

    [Fact]
    public void Parse_NoArgs_AllRequiredFromEnvVars_ReturnsCliArgs()
    {
        Environment.SetEnvironmentVariable("METABOLISM_PORT", "7100");
        Environment.SetEnvironmentVariable("METABOLISM_BROKER_URL", "http://from-env");
        Environment.SetEnvironmentVariable("METABOLISM_MODE", "produces");

        var result = CliArgs.Parse(Array.Empty<string>());

        result.Should().NotBeNull();
        result!.Port.Should().Be(7100);
        result.BrokerUrl.Should().Be("http://from-env");
        result.Mode.Should().Be("produces");
    }

    [Fact]
    public void Parse_ArgsTakePrecedenceOverEnvVars()
    {
        Environment.SetEnvironmentVariable("METABOLISM_PORT", "1111");
        Environment.SetEnvironmentVariable("METABOLISM_BROKER_URL", "http://env-broker");
        Environment.SetEnvironmentVariable("METABOLISM_MODE", "produces");
        var args = new[] { "--port=2222", "--brokerUrl=http://cli-broker", "--mode=consumes" };

        var result = CliArgs.Parse(args);

        result.Should().NotBeNull();
        result!.Port.Should().Be(2222);
        result.BrokerUrl.Should().Be("http://cli-broker");
        result.Mode.Should().Be("consumes");
    }

    [Fact]
    public void Parse_EnvVarsCoverOptionalFlagsToo()
    {
        Environment.SetEnvironmentVariable("METABOLISM_PORT", "5100");
        Environment.SetEnvironmentVariable("METABOLISM_BROKER_URL", "http://broker");
        Environment.SetEnvironmentVariable("METABOLISM_MODE", "consumes");
        Environment.SetEnvironmentVariable("METABOLISM_TOKEN", "env-token");
        Environment.SetEnvironmentVariable("METABOLISM_SIGNING_KEY", "env-key");
        Environment.SetEnvironmentVariable("METABOLISM_ISSUER", "env-issuer");
        Environment.SetEnvironmentVariable("METABOLISM_AUDIENCE", "env-audience");

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
        // Baseline: scrubbed env (ctor) + empty args → still null.
        CliArgs.Parse(Array.Empty<string>()).Should().BeNull();
    }
}
