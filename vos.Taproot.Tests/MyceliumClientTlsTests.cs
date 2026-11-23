using FluentAssertions;
using Xunit;

namespace vos.Taproot.Tests;

// The command line checks the server's certificate unless VOS_INSECURE_TLS is set, because accepting any certificate lets anyone on the network pose as the broker.
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
