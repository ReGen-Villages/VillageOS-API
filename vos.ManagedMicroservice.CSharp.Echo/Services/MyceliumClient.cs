using vos.ManagedMicroservice.Shared;

namespace vos.ManagedMicroservice.CSharp.Echo.Services;

public class MyceliumClient : MyceliumClientBase
{
    public MyceliumClient(
        IHttpClientFactory httpClientFactory,
        ILogger<MyceliumClient> logger,
        string myceliumUrl,
        string? serviceToken = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken)
    {
    }

    /// <summary>Register with Mycelium as an endpoint service.</summary>
    public Task<bool> RegisterAsync(int port)
        => RegisterAsync(port, "Echo", "endpoint-service");
}
