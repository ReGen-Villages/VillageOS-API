using Microsoft.Extensions.Logging;

namespace vos.Service.Shared;

// The broker client for a service that adds nothing of its own: it only needs to register under
// its own name. Services with their own broker calls derive from MyceliumClientBase instead.
public sealed class EndpointServiceMyceliumClient : MyceliumClientBase
{
    public const string StartCommand = "endpoint-service";

    private readonly string _serviceName;

    public EndpointServiceMyceliumClient(
        IHttpClientFactory httpClientFactory,
        ILogger<EndpointServiceMyceliumClient> logger,
        string serviceName,
        string myceliumUrl,
        string? serviceToken = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken)
    {
        _serviceName = serviceName;
    }

    public Task<bool> RegisterAsync(int port) => RegisterAsync(port, _serviceName, StartCommand);
}
