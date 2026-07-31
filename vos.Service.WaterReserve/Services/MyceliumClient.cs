using vos.Service.Shared;

namespace vos.Service.WaterReserve.Services;

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
