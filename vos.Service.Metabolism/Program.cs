using vos.Service.Auth;
using vos.Service.Metabolism.Configuration;
using vos.Service.Shared.Configuration;
using vos.Service.Metabolism.Endpoints;
using vos.Service.Metabolism.Services;
using vos.Service.Shared;
using vos.Service.Shared.Hosting;
using vos.Service.Shared.Middleware;
using vos.Service.Shared.Subscriptions;
using Serilog;


var builder = WebApplication.CreateBuilder(args);

var launchSettings = MetabolismLaunchSettings.Parse(args, builder.Configuration);
if (launchSettings == null)
{
    Console.WriteLine(MetabolismLaunchSettings.UsageMessage);
    Environment.Exit(1);
    return 1;
}

var servicePort = launchSettings.Service.Port;
var myceliumUrl = launchSettings.Service.MyceliumUrl;
var direction = launchSettings.Direction;
var launchToken = builder.Services.AddLaunchToken(launchSettings.Service.Token, myceliumUrl);
var apiKey = launchSettings.Service.ApiKey;
var verificationKey = launchSettings.Service.VerificationKey;

var isTestingEnv = builder.Environment.IsEnvironment("Testing");
ServiceHost.ConfigureLogging("Metabolism", $"metabolism-{direction.LaunchArgument}-.log", launchSettings.Service.LogDirectory, writeToFile: !isTestingEnv);

try
{
    Log.Information("VOS '{Mode}' Metabolism Service — Port: {Port}, Mycelium: {MyceliumUrl}",
        direction, servicePort, myceliumUrl);

    builder.Host.UseSerilog();
    builder.WebHost.UseUrls($"http://localhost:{servicePort}");
    builder.Services.AddHttpClient();
    builder.Services.AddContractValidation();

    var authEnabled = builder.AddMyceliumTokenAuthWhenKeyed(
        verificationKey, launchSettings.Service.Issuer, launchSettings.Service.Audience);

    builder.Services.AddSingleton(sp =>
        new MyceliumClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<MyceliumClient>>(),
            myceliumUrl, direction, launchToken, apiKey: apiKey));
    builder.Services.AddSingleton(sp =>
        new Metabolism(
            sp.GetRequiredService<MyceliumClient>(),
            sp.GetRequiredService<ILogger<Metabolism>>(),
            direction));
    builder.Services.AddSingleton<HandleRequestProcessor>();
    builder.Services.AddServiceTokenExchange(myceliumUrl);
    builder.Services.AddSingleton<ISubscriptionClient>(sp =>
        new SubscriptionClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<SubscriptionClient>>(),
            myceliumUrl, launchToken, apiKey: apiKey));
    builder.Services.AddHostedService<MetabolismSubscriptionService>();

    var requestCount = 0;
    var app = builder.Build();

    // Must precede RequestContractValidationMiddleware, which reads endpoint metadata via context.GetEndpoint().
    app.UseRouting();

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMyceliumModelToken();
    }

    app.UseRequestContractValidation();

    // Skipped under tests — the background shutdown task would race test-host teardown and pollute output.
    if (!app.Environment.IsEnvironment("Testing"))
    {
        var metabolism = app.Services.GetRequiredService<Metabolism>();

        app.Lifetime.ApplicationStopping.Register(() => _ = Task.Run(async () =>
        {
            try
            {
                Log.Information("Shutting down '{Mode}' handler — stopping {Count} simulation(s), {Requests} registration(s) processed",
                    direction, metabolism.GetAll().Count(), requestCount);
                await metabolism.StopAllAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during Metabolism shutdown");
            }
        }));
    }

    app.MapMetabolismEndpoints(
        direction,
        () => requestCount, () => requestCount++,
        authEnabled: authEnabled);

    app.Run();
}
catch (Exception error)
{
    return ServiceHost.FailedToStart("Metabolism", error);
}
finally
{
    Log.CloseAndFlush();
}

return 0;

// Elevates the implicit top-level Program class to public so WebApplicationFactory<Program> can name it in tests.
public partial class Program { }
