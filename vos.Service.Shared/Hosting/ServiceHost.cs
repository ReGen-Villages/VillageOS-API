using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace vos.Service.Shared.Hosting;

// The startup every service shares: where it writes its log, how it announces itself to the broker,
// and the endpoints the broker calls to check on it and stop it. What a service does with a request
// is its own; everything here is not.
public static class ServiceHost
{
    private const int ShutdownDelayMilliseconds = 300;

    // Told to the broker at registration, so its log routes can find this service's file.
    public static string? LogFilePrefix { get; private set; }

    // Only a folder the service was given is told, because the broker finds the default one beside the executable its connection declares.
    public static string? LogDirectory { get; private set; }

    public static string LogFilePrefixOf(string logFileName) => Path.GetFileNameWithoutExtension(logFileName);

    private const string LogLineTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";

    // Whatever started the service captures its standard output, so every line goes to the console in every environment.
    public static void ConfigureLogging(string serviceName, string logFileName, string? logDirectory, bool writeToFile = true)
    {
        LogFilePrefix = writeToFile ? LogFilePrefixOf(logFileName) : null;
        LogDirectory = writeToFile && logDirectory is not null ? Path.GetFullPath(logDirectory) : null;
        var configuration = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName)
            .WriteTo.Console(outputTemplate: LogLineTemplate);

        // Tests leave the file sink off: writing files from shared build agents invites flakiness.
        if (writeToFile)
        {
            var folder = logDirectory ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "logs");

            configuration = configuration.WriteTo.File(
                path: Path.Combine(folder, logFileName),
                rollingInterval: RollingInterval.Day,
                outputTemplate: LogLineTemplate,
                shared: true);
        }

        Log.Logger = configuration.CreateLogger();
    }

    // Whatever started the service reads its exit code and standard error, not its log file. A test host stops a
    // service by throwing HostAbortedException out of its entry point, which is not a failure.
    public static int FailedToStart(string serviceName, Exception error)
    {
        if (error is HostAbortedException) ExceptionDispatchInfo.Throw(error);
        Log.Fatal(error, "{Service} terminated unexpectedly", serviceName);
        Console.Error.WriteLine($"{serviceName} could not start: {error.Message}");
        return 1;
    }

    public static IServiceCollection AddMyceliumRegistration(
        this IServiceCollection services, string serviceName, int port) =>
        services.AddHostedService(provider => new MyceliumRegistration(
            provider.GetRequiredService<EndpointServiceMyceliumClient>(), serviceName, port));

    /// <summary>The clock this service stamps model values with, kept on the broker's. Registered as
    /// <see cref="ModelClock"/> rather than as the injected <c>TimeProvider</c>, because a service also
    /// measures real durations of its own — how long a verification code stays good, when a token is due
    /// for replacement — and those are not the model's business however fast a simulation runs.</summary>
    public static IServiceCollection AddModelClock<TClient>(this IServiceCollection services, string serviceName)
        where TClient : MyceliumClientBase
    {
        services.AddSingleton<ModelClock>();
        return services.AddHostedService(provider => new ModelClockFollower(
            provider.GetRequiredService<ModelClock>(),
            cancellationToken => provider.GetRequiredService<TClient>().ReadModelTimeAsync(cancellationToken),
            provider.GetRequiredService<ILogger<ModelClockFollower>>(),
            serviceName,
            ModelClockInterval));
    }

    // Short enough that a service launched before a simulation anchors the clock is stamping model
    // instants within seconds of the anchor, and one local request either way is nothing beside what
    // a service does for a single call it handles.
    public static readonly TimeSpan ModelClockInterval = TimeSpan.FromSeconds(5);

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
