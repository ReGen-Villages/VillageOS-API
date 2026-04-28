using vos.Auth.Shared;
using vos.ManagedMicroservice.Metabolism.Configuration;
using vos.ManagedMicroservice.Metabolism.Endpoints;
using vos.ManagedMicroservice.Metabolism.Services;
using Serilog;

var cliArgs = CliArgs.Parse(args);
if (cliArgs == null)
{
    Console.WriteLine(CliArgs.UsageMessage);
    Environment.Exit(1);
    return; // unreachable but helps flow analysis
}

var servicePort = cliArgs.Port;
var brokerUrl = cliArgs.BrokerUrl;
var mode = cliArgs.Mode;
var serviceToken = cliArgs.Token;
var signingKey = cliArgs.SigningKey;

// Configure Serilog
var logPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "logs", $"metabolism-{mode}-.log");
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", $"Metabolism-{mode}")
    .WriteTo.File(
        path: logPath,
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
        shared: true)
    .CreateLogger();

try
{
    Log.Information("VOS '{Mode}' Metabolism Service — Port: {Port}, Broker: {BrokerUrl}",
        mode, servicePort, brokerUrl);

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();
    builder.WebHost.UseUrls($"http://localhost:{servicePort}");
    builder.Services.AddHttpClient();

    // Add JWT auth if broker provided a signing key
    if (!string.IsNullOrEmpty(signingKey))
    {
        builder.AddBrokerTokenAuth(signingKey);
        Log.Information("JWT authentication enabled for incoming broker requests");
    }

    var requestCount = 0;
    var app = builder.Build();

    // Enable auth middleware when signing key is configured
    if (!string.IsNullOrEmpty(signingKey))
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }

    // Initialize services
    var httpClientFactory = app.Services.GetRequiredService<IHttpClientFactory>();
    var logger = app.Services.GetRequiredService<ILogger<BrokerClient>>();
    var brokerClient = new BrokerClient(httpClientFactory, logger, brokerUrl, mode, serviceToken);

    var metabolismLogger = app.Services.GetRequiredService<ILogger<Metabolism>>();
    var metabolism = new Metabolism(brokerClient, metabolismLogger, mode);

    var processorLogger = app.Services.GetRequiredService<ILogger<HandleRequestProcessor>>();
    var processor = new HandleRequestProcessor(metabolism, processorLogger);

    // Subscribe to relationship property changes so simulations update live
    brokerClient.OnRelationshipPropertyChanged += (relId, propName, value) =>
        metabolism.UpdateProperty(relId.ToString(), propName, value);

    // Connect SignalR for live property updates
    app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () =>
    {
        try
        {
            await brokerClient.ConnectSignalRAsync(app.Lifetime.ApplicationStopping);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error connecting SignalR during Metabolism startup");
        }
    }));

    // Stop simulations and deregister from broker on shutdown
    app.Lifetime.ApplicationStopping.Register(() => _ = Task.Run(async () =>
    {
        try
        {
            Log.Information("Shutting down '{Mode}' handler — stopping {Count} simulation(s), {Requests} registration(s) processed",
                mode, metabolism.GetAll().Count(), requestCount);
            await metabolism.StopAllAsync();
            await brokerClient.DeregisterAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during Metabolism shutdown");
        }
    }));

    app.MapMetabolismEndpoints(
        processor, metabolism, brokerClient, mode,
        () => requestCount, () => requestCount++,
        authEnabled: !string.IsNullOrEmpty(signingKey));

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Metabolism terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
