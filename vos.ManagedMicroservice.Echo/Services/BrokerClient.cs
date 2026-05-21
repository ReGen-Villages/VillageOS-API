using vos.ManagedMicroservice.Shared;

namespace vos.ManagedMicroservice.Echo.Services;

/// <summary>
/// Broker client for the Echo endpoint service.
/// Extends BrokerClientBase with shared token management and registration.
/// </summary>
public class BrokerClient : BrokerClientBase
{
    public BrokerClient(
        IHttpClientFactory httpClientFactory,
        ILogger<BrokerClient> logger,
        string brokerUrl,
        string? serviceToken = null)
        : base(httpClientFactory, logger, brokerUrl, serviceToken)
    {
    }

    /// <summary>Register with the broker as an endpoint service.</summary>
    public Task<bool> RegisterAsync(int port)
        => RegisterAsync(port, "Echo", "endpoint-service");
}
