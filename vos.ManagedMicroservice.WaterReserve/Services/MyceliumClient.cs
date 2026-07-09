using vos.ManagedMicroservice.Shared;

namespace vos.ManagedMicroservice.WaterReserve.Services;

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

    public Task<bool> RegisterAsync(int port)
        => RegisterAsync(port, "WaterReserve", "endpoint-service");
}
