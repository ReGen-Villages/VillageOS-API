using System;
using System.Net.Http;
using FluentAssertions;
using vos.Taproot;
using Xunit;

namespace vos.Taproot.Tests;

/// <summary>
/// #5506: the CLI must validate TLS certificates by default and bypass validation only when
/// VOS_INSECURE_TLS is explicitly set (previously it accepted any certificate — MITM).
/// </summary>
[Collection(nameof(CliEnvVarCollection))]
public class MyceliumClientTlsTests : IDisposable
{
    public void Dispose() => Environment.SetEnvironmentVariable("VOS_INSECURE_TLS", null);

    [Fact]
    public void Validates_certificates_by_default()
    {
        Environment.SetEnvironmentVariable("VOS_INSECURE_TLS", null);
        using var handler = MyceliumClient.CreateHandler();
        handler.ServerCertificateCustomValidationCallback.Should().BeNull("default validation must apply");
    }

    [Fact]
    public void Bypasses_validation_only_when_explicitly_opted_in()
    {
        Environment.SetEnvironmentVariable("VOS_INSECURE_TLS", "true");
        using var handler = MyceliumClient.CreateHandler();
        handler.ServerCertificateCustomValidationCallback
            .Should().BeSameAs(HttpClientHandler.DangerousAcceptAnyServerCertificateValidator);
    }
}
