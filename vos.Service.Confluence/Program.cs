using vos.Auth.Shared;
using vos.Service.Confluence.Models;
using vos.Service.Confluence.Services;
using vos.Service.Shared;
using vos.Service.Shared.Configuration;
using vos.Service.Shared.Hosting;
using vos.Service.Shared.Subscriptions;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var launchSettings = ServiceLaunchSettings.Parse(args, builder.Configuration);
if (launchSettings == null)
{
    Console.WriteLine(ServiceLaunchSettings.UsageMessage);
    Environment.Exit(1);
    return;
}

var servicePort = launchSettings.Port;
var myceliumUrl = launchSettings.MyceliumUrl;
var serviceToken = launchSettings.Token;
var signingKey = launchSettings.SigningKey;

var isTestingEnv = builder.Environment.IsEnvironment("Testing");
ServiceHost.ConfigureLogging("Confluence", "confluence-.log", writeToFile: !isTestingEnv);

try
{
    Log.Information("VillageOS Confluence Service - Port: {Port}, Mycelium: {MyceliumUrl}", servicePort, myceliumUrl);

    builder.Host.UseSerilog();
    builder.WebHost.UseUrls($"http://localhost:{servicePort}");
    builder.Services.AddHttpClient();

    var authEnabled = !string.IsNullOrEmpty(signingKey);
    if (authEnabled)
    {
        builder.AddMyceliumTokenAuth(
            signingKey!,
            issuer: launchSettings.Issuer,
            audience: launchSettings.Audience);
        Log.Information("JWT authentication enabled for incoming mycelium requests (issuer={Issuer}, audience={Audience})",
            launchSettings.Issuer, launchSettings.Audience);
    }

    // Coverage is edges, so the only read this service makes is a scoped snapshot.
    builder.Services.AddSingleton<ISubscriptionClient>(sp =>
        new SubscriptionClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<SubscriptionClient>>(),
            myceliumUrl,
            serviceToken));
    builder.Services.AddSingleton<CoveringSourceService>();

    var app = builder.Build();

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMyceliumModelToken();
    }

    var handleEndpoint = app.MapPost("/handle", async (
        DiscoveryRequest request,
        CoveringSourceService coveringSources,
        HttpContext httpContext) =>
    {
        if (request.SiteId == Guid.Empty)
            return Results.Json(new { error = "Request must include a siteId." }, statusCode: 400);

        var covering = await coveringSources.ForSiteAsync(request.SiteId, httpContext.RequestAborted);
        if (covering == null)
            return Results.Problem(
                detail: "Failed to read which sources cover the site. Refusing rather than reporting that none do.",
                statusCode: 502,
                title: "Coverage resolution failed");

        return Results.Ok(new
        {
            siteId = request.SiteId,
            covering = covering.Select(source => new
            {
                dataSourceId = source.DataSourceId,
                name = source.Name,
                endpointName = source.EndpointName
            })
        });
    });
    if (authEnabled) handleEndpoint.RequireAuthorization();

    app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "Confluence" }));

    var shutdownEndpoint = app.MapPost("/shutdown", (IHostApplicationLifetime lifetime) =>
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(300);
            lifetime.StopApplication();
        });

        return Results.Ok(new { message = "Shutting down Confluence" });
    });
    if (authEnabled) shutdownEndpoint.RequireAuthorization();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Confluence terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Exposed to WebApplicationFactory<Program> in the test project per docs/SERVICES.md.
public partial class Program { }
