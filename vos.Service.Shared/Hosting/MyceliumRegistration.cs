using Microsoft.Extensions.Hosting;
using Serilog;

namespace vos.Service.Shared.Hosting;

// Announces the service to the broker when it comes up. It does not withdraw the registration on
// shutdown: the broker's liveness monitor removes a registration after repeated failed health checks,
// unless the broker supervises the service, and a service may delete only the registration it made itself.
internal sealed class MyceliumRegistration : IHostedService
{
    private readonly EndpointServiceMyceliumClient _client;
    private readonly string _serviceName;
    private readonly int _port;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _stopping = new();

    public MyceliumRegistration(EndpointServiceMyceliumClient client, string serviceName, int port, TimeProvider time)
    {
        _client = client;
        _serviceName = serviceName;
        _port = port;
        _time = time;
    }

    // Registration runs off the startup path: a broker that is slow, absent or refusing must not
    // stop the service coming up, so the service serves while it is asked again.
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var stopping = _stopping.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var registered = await _client.RegisterUntilSettledAsync(
                    _port, ServiceHost.LogFilePrefix, ServiceHost.LogDirectory, _time, stopping);
                Log.Information("{Service} service {Status} with mycelium",
                    _serviceName, registered ? "registered" : "failed to register");
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
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

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping.Cancel();
        return Task.CompletedTask;
    }
}
