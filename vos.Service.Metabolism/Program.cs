using vos.Auth.Shared;
using vos.Service.Metabolism.Configuration;
using vos.Service.Shared.Configuration;
using vos.Service.Metabolism.Endpoints;
using vos.Service.Metabolism.Services;
using vos.Service.Shared;
using vos.Service.Shared.Middleware;
using vos.Service.Shared.Subscriptions;
using Serilog;


var builder = WebApplication.CreateBuilder(args);

var launchSettings = MetabolismLaunchSettings.Parse(args, builder.Configuration);
if (launchSettings == null)
{
    Console.WriteLine(MetabolismLaunchSettings.UsageMessage);
    Environment.Exit(1);
    return;
}

var servicePort = launchSettings.Service.Port;
var myceliumUrl = launchSettings.Service.MyceliumUrl;
var mode = launchSettings.Mode;
var serviceToken = launchSettings.Service.Token;
var signingKey = launchSettings.Service.SigningKey;

// Skip the file sink under WebApplicationFactory<Program> tests — file I/O invites flakiness on shared CI agents.
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

    // Bug #5391: validate with the issuer/audience Mycelium passes via CLI, not library defaults —
    // defaults diverged from Mycelium config and silently accepted nothing in production.
    if (!string.IsNullOrEmpty(signingKey))
    {
        builder.AddMyceliumTokenAuth(
            signingKey,
            issuer: launchSettings.Service.Issuer,
            audience: launchSettings.Service.Audience);
        Log.Information("JWT authentication enabled for incoming mycelium requests (issuer={Issuer}, audience={Audience})",
            launchSettings.Service.Issuer, launchSettings.Service.Audience);
    }

    builder.Services.AddSingleton(sp =>
        new MyceliumClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<MyceliumClient>>(),
            myceliumUrl, mode, serviceToken));
    builder.Services.AddSingleton(sp =>
        new Metabolism(
            sp.GetRequiredService<MyceliumClient>(),
            sp.GetRequiredService<ILogger<Metabolism>>(),
            mode));
    builder.Services.AddSingleton<HandleRequestProcessor>();
    builder.Services.AddSingleton<ISubscriptionClient>(sp =>
        new SubscriptionClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<SubscriptionClient>>(),
            myceliumUrl, serviceToken));
    builder.Services.AddHostedService<MetabolismSubscriptionService>();

    var requestCount = 0;
    var app = builder.Build();

    // Must precede RequestContractValidationMiddleware, which reads endpoint metadata via context.GetEndpoint().
    app.UseRouting();

    if (!string.IsNullOrEmpty(signingKey))
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMyceliumRequestToken();
    }

    app.UseRequestContractValidation();

    // Skip shutdown deregister under tests — synthetic Mycelium URL would fail in a background task and pollute output.
    if (!app.Environment.IsEnvironment("Testing"))
    {
        var myceliumClient = app.Services.GetRequiredService<MyceliumClient>();
        var metabolism = app.Services.GetRequiredService<Metabolism>();

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

// Elevates the implicit top-level Program class to public so WebApplicationFactory<Program> can name it in tests.
public partial class Program { }
