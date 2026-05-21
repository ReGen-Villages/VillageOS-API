using Microsoft.Extensions.Logging;
using vos.ManagedMicroservice.Shared;

namespace vos.ManagedMicroservice.Shared.Tests;

// Thin subclass that exposes the protected CreateAuthenticatedClientAsync so the
// base class's authenticated-client construction can be exercised directly.
internal sealed class TestableBrokerClient : BrokerClientBase
{
    public TestableBrokerClient(IHttpClientFactory httpClientFactory, ILogger logger, string brokerUrl, string? serviceToken = null)
        : base(httpClientFactory, logger, brokerUrl, serviceToken)
    {
    }

    public Task<HttpClient> CreateAuthenticatedClientPublicAsync(TimeSpan? timeout = null)
        => CreateAuthenticatedClientAsync(timeout);
}
