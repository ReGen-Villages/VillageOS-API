using vos.Auth.Shared;
using vos.Service.Confluence.Configuration;
using vos.Service.Confluence.Models;
using vos.Service.Confluence.Services;
using vos.Service.Shared;
using vos.Service.Shared.Hosting;
using vos.Service.Shared.Subscriptions;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var launchSettings = ConfluenceLaunchSettings.Parse(args, builder.Configuration);
if (launchSettings == null)
{
    Console.WriteLine(ConfluenceLaunchSettings.UsageMessage);
    Environment.Exit(1);
    return;
}

var servicePort = launchSettings.Service.Port;
var myceliumUrl = launchSettings.Service.MyceliumUrl;
var serviceToken = launchSettings.Service.Token;
var apiKey = launchSettings.Service.ApiKey;
var verificationKey = launchSettings.Service.VerificationKey;

var isTestingEnv = builder.Environment.IsEnvironment("Testing");
ServiceHost.ConfigureLogging("Confluence", "confluence-.log", writeToFile: !isTestingEnv);

try
{
    Log.Information(
        "VillageOS Confluence Service - Port: {Port}, Mycelium: {MyceliumUrl}, fetching through {Subdomain}",
        servicePort, myceliumUrl, launchSettings.FetcherSubdomain);

    builder.Host.UseSerilog();
    builder.WebHost.UseUrls($"http://localhost:{servicePort}");
    builder.Services.AddHttpClient();

    var authEnabled = !string.IsNullOrEmpty(verificationKey);
    if (authEnabled)
    {
        builder.AddMyceliumTokenAuth(
            verificationKey!,
            issuer: launchSettings.Service.Issuer,
            audience: launchSettings.Service.Audience);
        Log.Information("JWT authentication enabled for incoming mycelium requests (issuer={Issuer}, audience={Audience})",
            launchSettings.Service.Issuer, launchSettings.Service.Audience);
    }

    // Coverage is edges, so the only read this service makes is a scoped snapshot.
    builder.Services.AddSingleton<ISubscriptionClient>(sp =>
        new SubscriptionClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<SubscriptionClient>>(),
            myceliumUrl,
            serviceToken, apiKey: apiKey));
    builder.Services.AddSingleton<CoveringSourceService>();
    builder.Services.AddSingleton<ISourceFetcher>(sp =>
        new EndpointServiceSourceFetcher(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<EndpointServiceSourceFetcher>>(),
            myceliumUrl,
            serviceToken,
            launchSettings.FetcherSubdomain,
            launchSettings.SourceTimeout,
            apiKey));
    builder.Services.AddSingleton(sp =>
        new DiscoveryRunner(
            sp.GetRequiredService<ISourceFetcher>(),
            launchSettings.MaxConcurrentSources,
            sp.GetRequiredService<ILogger<DiscoveryRunner>>()));
    builder.Services.AddSingleton(sp =>
        new MyceliumRelationshipClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<MyceliumRelationshipClient>>(),
            myceliumUrl,
            serviceToken, apiKey: apiKey));
    builder.Services.AddSingleton<AnalysisSpawner>();

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
        DiscoveryRunner runner,
        AnalysisSpawner analysis,
        HttpContext httpContext) =>
    {
        if (request.SiteId == Guid.Empty)
            return Results.Json(new { error = "Request must include a siteId." }, statusCode: 400);

        var coverage = await coveringSources.ForSiteAsync(request.SiteId, httpContext.RequestAborted);
        if (coverage == null)
            return Results.Problem(
                detail: "Failed to read which sources cover the site. Refusing rather than reporting that none do.",
                statusCode: 502,
                title: "Coverage resolution failed");

        var report = await runner.RunAsync(
            request.SiteId, coverage.Covering, coverage.Values, httpContext.RequestAborted);

        // Whatever mixture resolved, including none. The analysis reports against what discovery left
        // it, and a site whose sources were all unavailable is exactly the case a planner needs the
        // analysis to say something about rather than silently never running.
        var spawn = await analysis.SpawnAsync(
            request.SiteId, coverage.Analysis, httpContext.RequestAborted);

        return Results.Ok(new
        {
            siteId = report.SiteId,
            resolved = report.Resolved.Select(outcome => outcome.Source),
            unresolved = report.Unresolved.Select(outcome => new
            {
                source = outcome.Source,
                reason = outcome.Reason
            }),
            analysis = new { started = spawn.Started, reason = spawn.Reason }
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
