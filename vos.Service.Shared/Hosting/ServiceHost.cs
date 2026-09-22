using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace vos.Service.Shared.Hosting;

// The startup every service shares: where it writes its log, how it announces itself to the broker,
// and the endpoints the broker calls to check on it and stop it. What a service does with a request
// is its own; everything here is not.
public static class ServiceHost
{
    private const int ShutdownDelayMilliseconds = 300;

    public static void ConfigureLogging(string serviceName, string logFileName, bool writeToFile = true)
    {
        var configuration = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName);

        // Tests leave the file sink off: writing files from shared build agents invites flakiness.
        if (writeToFile)
        {
            var logPath = Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "logs", logFileName);

            configuration = configuration.WriteTo.File(
                path: logPath,
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                shared: true);
        }

        Log.Logger = configuration.CreateLogger();
    }

    public static IServiceCollection AddMyceliumRegistration(
        this IServiceCollection services, string serviceName, int port) =>
        services.AddHostedService(provider => new MyceliumRegistration(
            provider.GetRequiredService<EndpointServiceMyceliumClient>(), serviceName, port));

    /// <summary>The clock this service stamps model values with, kept on the broker's. Registered as
    /// <see cref="ModelClock"/> rather than as the injected <c>TimeProvider</c>, because a service also
    /// measures real durations of its own — how long a verification code stays good, when a token is due
    /// for replacement — and those are not the model's business however fast a simulation runs.</summary>
    public static IServiceCollection AddModelClock<TClient>(
        this IServiceCollection services, string serviceName, TimeSpan? interval = null)
        where TClient : MyceliumClientBase
    {
        services.AddSingleton<ModelClock>();
        return services.AddHostedService(provider => new ModelClockFollower(
            provider.GetRequiredService<ModelClock>(),
            cancellationToken => provider.GetRequiredService<TClient>().ReadModelTimeAsync(cancellationToken),
            serviceName,
            interval ?? ModelClockInterval));
    }

    // Short enough that a service launched before a simulation anchors the clock is stamping model
    // instants within seconds of the anchor, and one local request either way is nothing beside what
    // a service does for a single call it handles.
    private static readonly TimeSpan ModelClockInterval = TimeSpan.FromSeconds(5);

    // Returns nothing to gate on, unlike MapShutdown: the broker polls /health to decide whether the
    // service is alive, and it cannot do that behind authentication. The process id is how the broker
    // measures a service it did not start itself — it holds no handle to such a process.
    public static void MapHealth(this WebApplication app, string serviceName) =>
        app.MapGet("/health", () => new { status = "Healthy", service = serviceName, processId = Environment.ProcessId });

    // For a service the broker registers. The statistics name its registration, so a service that keeps no
    // registration maps health alone.
    public static void MapHealthAndStats(
        this WebApplication app, string serviceName, string myceliumUrl)
    {
        app.MapHealth(serviceName);

        app.MapGet("/stats", (EndpointServiceMyceliumClient client) => new
        {
            service = serviceName,
            version = "1.0.0",
            handlerId = client.HandlerId.ToString(),
            myceliumUrl,
        });
    }

    // The reply is sent before the host stops, so the caller sees the acknowledgement rather than a
    // dropped connection.
    public static RouteHandlerBuilder MapShutdown(this WebApplication app, string serviceName)
    {
        return app.MapPost("/shutdown", (IHostApplicationLifetime lifetime) =>
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(ShutdownDelayMilliseconds);
                lifetime.StopApplication();
            });

            return new { message = $"Shutting down {serviceName} service" };
        });
    }
}
