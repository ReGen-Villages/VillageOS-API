using Microsoft.Extensions.Logging;
using vos.ManagedMicroservice.Shared;
using vos.ManagedMicroservice.Shared.Contracts.Validation;

namespace vos.ManagedMicroservice.Shared.Tests;

// Thin subclass that exposes the protected CreateAuthenticatedClientAsync so the
// base class's authenticated-client construction can be exercised directly, plus
// a settable override of OutboundViolationMode so Phase 3 validation tests pin
// both Throw and Log paths regardless of the test assembly's build configuration.
internal sealed class TestableMyceliumClient : MyceliumClientBase
{
    public TestableMyceliumClient(IHttpClientFactory httpClientFactory, ILogger logger, string myceliumUrl, string? serviceToken = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken)
    {
    }

    public SchemaViolationMode? ViolationModeForTests { get; set; }

    protected override SchemaViolationMode OutboundViolationMode =>
        ViolationModeForTests ?? base.OutboundViolationMode;

    public Task<HttpClient> CreateAuthenticatedClientPublicAsync(TimeSpan? timeout = null)
        => CreateAuthenticatedClientAsync(timeout);
}
