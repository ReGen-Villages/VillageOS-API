using vos.Auth.Shared;
using vos.ManagedMicroservice.Metabolism.Configuration;
using vos.ManagedMicroservice.Metabolism.Endpoints;
using vos.ManagedMicroservice.Metabolism.Services;
using vos.ManagedMicroservice.Shared.Middleware;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var cliArgs = CliArgs.Parse(args, builder.Configuration);
if (cliArgs == null)
{
    Console.WriteLine(CliArgs.UsageMessage);
    Environment.Exit(1);
    return; // unreachable but helps flow analysis
}

var servicePort = cliArgs.Port;
var myceliumUrl = cliArgs.MyceliumUrl;
var mode = cliArgs.Mode;
var serviceToken = cliArgs.Token;
var signingKey = cliArgs.SigningKey;

// Configure Serilog. Skip the file sink when running under WebApplicationFactory<Program>
// tests — file I/O under the test host has no value and invites flakiness on shared CI agents.
var isTestingEnv = builder.Environment.IsEnvironment("Testing");

var loggerConfig = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", $"Metabolism-{mode}");

if (!isTestingEnv)
{
    var logPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "logs", $"metabolism-{mode}-.log");
    loggerConfig = loggerConfig.WriteTo.File(
        path: logPath,
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
        shared: true);
}

Log.Logger = loggerConfig.CreateLogger();

try
{
    Log.Information("VOS '{Mode}' Metabolism Service — Port: {Port}, Mycelium: {MyceliumUrl}",
        mode, servicePort, myceliumUrl);

    builder.Host.UseSerilog();
    builder.WebHost.UseUrls($"http://localhost:{servicePort}");
    builder.Services.AddHttpClient();
    builder.Services.AddContractValidation();

    // Add JWT auth if mycelium provided a signing key. Bug #5391: use the
    // issuer/audience Mycelium passes via CLI so validation matches what
    // Mycelium signed; falling back to the hardcoded library defaults
    // silently accepted nothing in production because Mycelium config
    // diverged from the daemon defaults.
    if (!string.IsNullOrEmpty(signingKey))
    {
        builder.AddMyceliumTokenAuth(
            signingKey,
            issuer: cliArgs.Issuer ?? "VillageOS",
            audience: cliArgs.Audience ?? "VosClients");
        Log.Information("JWT authentication enabled for incoming mycelium requests (issuer={Issuer}, audience={Audience})",
            cliArgs.Issuer ?? "VillageOS", cliArgs.Audience ?? "VosClients");
    }

    builder.Services.AddSingleton<IHubConnectionFactory, DefaultHubConnectionFactory>();
    builder.Services.AddSingleton(sp =>
        new MyceliumClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<MyceliumClient>>(),
            myceliumUrl, mode, serviceToken,
            sp.GetRequiredService<IHubConnectionFactory>()));
    builder.Services.AddSingleton(sp =>
        new Metabolism(
            sp.GetRequiredService<MyceliumClient>(),
            sp.GetRequiredService<ILogger<Metabolism>>(),
            mode));
    builder.Services.AddSingleton<HandleRequestProcessor>();
    builder.Services.AddHostedService<MetabolismEventSubscriber>();

    var requestCount = 0;
    var app = builder.Build();

    // UseRouting is required before middleware that inspects endpoint metadata
    // (RequestContractValidationMiddleware reads ContractValidationMetadata via
    // context.GetEndpoint()). WebApplication auto-inserts UseEndpoints at the end.
    app.UseRouting();

    // Enable auth middleware when signing key is configured
    if (!string.IsNullOrEmpty(signingKey))
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }

    app.UseRequestContractValidation();

    // Connect SignalR for live property updates. Skip under WebApplicationFactory<Program>
    // tests — Mycelium URL is synthetic, the connection would fail in a background task,
    // and the noise (failed retries) pollutes test output.
    if (!app.Environment.IsEnvironment("Testing"))
    {
        var myceliumClient = app.Services.GetRequiredService<MyceliumClient>();
        var metabolism = app.Services.GetRequiredService<Metabolism>();

        app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () =>
        {
            try
            {
                await myceliumClient.ConnectSignalRAsync(app.Lifetime.ApplicationStopping);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error connecting SignalR during Metabolism startup");
            }
        }));

        // Stop simulations and deregister from mycelium on shutdown
        app.Lifetime.ApplicationStopping.Register(() => _ = Task.Run(async () =>
        {
            try
            {
                Log.Information("Shutting down '{Mode}' handler — stopping {Count} simulation(s), {Requests} registration(s) processed",
                    mode, metabolism.GetAll().Count(), requestCount);
                await metabolism.StopAllAsync();
                await myceliumClient.DeregisterAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during Metabolism shutdown");
            }
        }));
    }

    app.MapMetabolismEndpoints(
        mode,
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

// Exposed to WebApplicationFactory<Program> in the test project per docs/MICROSERVICES.md.
// Top-level statements compile to a `Program` class that is internal by default — this empty
// partial declaration just elevates it to public so the test factory can name it.
public partial class Program { }
