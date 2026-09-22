using Microsoft.Extensions.Hosting;
using Serilog;

namespace vos.Service.Shared.Hosting;

// Announces the service to the broker when it comes up. It does not withdraw the registration on
// shutdown: that route is admin-only, so the call is refused whatever the service holds, and the
// broker's liveness monitor removes a registration whose service stops answering.
internal sealed class MyceliumRegistration : IHostedService
{
    private readonly EndpointServiceMyceliumClient _client;
    private readonly string _serviceName;
    private readonly int _port;

    public MyceliumRegistration(EndpointServiceMyceliumClient client, string serviceName, int port)
    {
        _client = client;
        _serviceName = serviceName;
        _port = port;
    }

    // Registration runs off the startup path: a broker that is slow, absent or refusing must not
    // stop the service coming up, so a failure is logged and the service serves regardless.
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var registered = await _client.RegisterAsync(_port);
                Log.Information("{Service} service {Status} with mycelium",
                    _serviceName, registered ? "registered" : "failed to register");
            }
            // RegisterAsync handles its own network failures but validates the payload outside that
            // guard, so a schema violation still reaches here. Unhandled, it would abort startup.
            catch (Exception exception)
            {
                Log.Error(exception, "Error during {Service} startup registration", _serviceName);
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
