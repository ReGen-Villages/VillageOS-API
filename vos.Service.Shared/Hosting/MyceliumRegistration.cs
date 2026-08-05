using Microsoft.Extensions.Hosting;
using Serilog;

namespace vos.Service.Shared.Hosting;

// Announces the service to the broker when it comes up and withdraws it when it goes down.
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
            catch (Exception exception)
            {
                Log.Error(exception, "Error during {Service} startup registration", _serviceName);
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }

    // Withdrawal is awaited, unlike registration: the host waits for this to finish, so the broker
    // learns the service is gone instead of being left with a handler that no longer answers.
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _client.DeregisterAsync();
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Error during {Service} shutdown deregistration", _serviceName);
        }
    }
}
