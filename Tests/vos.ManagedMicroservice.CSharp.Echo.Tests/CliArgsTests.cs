// Echo is the canonical reference implementation of a ManagedMicroservice.
// These tests document the CLI-args contract that EVERY microservice must
// satisfy. New microservice test projects (Metabolism, Tributary,
// Delta, future services) should mirror this file's shape:
// per-flag tests, port-bounds tests, and at least one Theory for invalid
// input. See docs/MICROSERVICES.md §10 for the full template.

using FluentAssertions;
using vos.ManagedMicroservice.CSharp.Echo.Configuration;
using Xunit;

namespace vos.ManagedMicroservice.CSharp.Echo.Tests;

public class CliArgsTests
{
    // ---- Template requirement: required flags ----

    [Fact]
    public void Parse_RequiredFlagsOnly_ReturnsArgsWithDefaultedOptionals_PerTemplate()
    {
        var result = CliArgs.Parse(new[] { "--port=7100", "--myceliumUrl=https://localhost:7243" });

        result.Should().NotBeNull();
        result!.Port.Should().Be(7100);
        result.MyceliumUrl.Should().Be("https://localhost:7243");
        result.Token.Should().BeNull();
        result.SigningKey.Should().BeNull();
        result.Issuer.Should().BeNull();
        result.Audience.Should().BeNull();
    }

    [Fact]
    public void Parse_MissingPort_ReturnsNull_PerTemplate()
    {
        CliArgs.Parse(new[] { "--myceliumUrl=https://localhost:7243" }).Should().BeNull();
    }

    [Fact]
    public void Parse_MissingMyceliumUrl_ReturnsNull_PerTemplate()
    {
        CliArgs.Parse(new[] { "--port=7100" }).Should().BeNull();
    }

    [Fact]
    public void Parse_NoArgs_ReturnsNull_PerTemplate()
    {
        CliArgs.Parse(Array.Empty<string>()).Should().BeNull();
    }

    // ---- Template requirement: port validation in [1, 65535] and integer-only ----

    [Theory]
    [InlineData("--port=0")]
    [InlineData("--port=-1")]
    [InlineData("--port=65536")]
    [InlineData("--port=99999")]
    [InlineData("--port=NaN")]
    [InlineData("--port=")]
    public void Parse_InvalidPort_ReturnsNull_PerTemplate(string portArg)
    {
        CliArgs.Parse(new[] { portArg, "--myceliumUrl=https://localhost:7243" }).Should().BeNull();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7100)]
    [InlineData(65535)]
    public void Parse_PortAtBoundaries_Accepted_PerTemplate(int port)
    {
        var result = CliArgs.Parse(new[] { $"--port={port}", "--myceliumUrl=https://localhost:7243" });
        result.Should().NotBeNull();
        result!.Port.Should().Be(port);
    }

    // ---- Template requirement: optional flags pass-through ----

    [Fact]
    public void Parse_AllOptionalFlags_PopulateRespectiveFields_PerTemplate()
    {
        var result = CliArgs.Parse(new[]
        {
            "--port=7100",
            "--myceliumUrl=https://localhost:7243",
            "--token=svc-jwt-abc",
            "--signingKey=YmFzZTY0a2V5",
            "--issuer=VillageOS",
            "--audience=VosClients"
        });

        result.Should().NotBeNull();
        result!.Token.Should().Be("svc-jwt-abc");
        result.SigningKey.Should().Be("YmFzZTY0a2V5");
        result.Issuer.Should().Be("VillageOS");
        result.Audience.Should().Be("VosClients");
    }

    [Fact]
    public void Parse_FlagOrderIndependent_PerTemplate()
    {
        // Standard convention: arg parsing scans for prefixes via FirstOrDefault, so order
        // is irrelevant. New microservices must preserve this property.
        var result = CliArgs.Parse(new[]
        {
            "--audience=VosClients",
            "--myceliumUrl=https://localhost:7243",
            "--port=7100",
            "--issuer=VillageOS",
            "--signingKey=YmFzZTY0a2V5",
            "--token=svc-jwt-abc"
        });

        result.Should().NotBeNull();
        result!.Port.Should().Be(7100);
        result.Token.Should().Be("svc-jwt-abc");
    }

    [Fact]
    public void Parse_OptionalFlagsAbsent_LeavesFieldsNull_PerTemplate()
    {
        var result = CliArgs.Parse(new[]
        {
            "--port=7100",
            "--myceliumUrl=https://localhost:7243",
            "--token=svc-jwt-abc"
            // signingKey, issuer, audience absent
        });

        result.Should().NotBeNull();
        result!.Token.Should().Be("svc-jwt-abc");
        result.SigningKey.Should().BeNull();
        result.Issuer.Should().BeNull();
        result.Audience.Should().BeNull();
    }

    // ---- Template requirement: UsageMessage documents every flag ----

    [Fact]
    public void UsageMessage_MentionsEverySupportedFlag_PerTemplate()
    {
        var usage = CliArgs.UsageMessage;

        usage.Should().Contain("--port");
        usage.Should().Contain("--myceliumUrl");
        usage.Should().Contain("--token");
        usage.Should().Contain("--signingKey");
        usage.Should().Contain("--issuer");
        usage.Should().Contain("--audience");
    }

    [Fact]
    public void UsageMessage_IsNonEmptyAndStartsWithUsage_PerTemplate()
    {
        CliArgs.UsageMessage.Should().StartWith("Usage:");
    }

    // ---- Template extension: extra unknown flags are ignored ----

    [Fact]
    public void Parse_UnknownFlag_IgnoredSilently_PerTemplate()
    {
        // Microservices must not crash on unknown flags so callers can pass forward-compat options.
        var result = CliArgs.Parse(new[]
        {
            "--port=7100",
            "--myceliumUrl=https://localhost:7243",
            "--unknownFlag=something"
        });

        result.Should().NotBeNull();
        result!.Port.Should().Be(7100);
    }

    // ---- Echo-specific: record value semantics ----

    [Fact]
    public void Record_Equality_PerCSharpRecordSemantics()
    {
        var a = new CliArgs(7100, "https://localhost:7243");
        var b = new CliArgs(7100, "https://localhost:7243");
        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }
}
