using vos.Auth.Shared;
using vos.Service.Forage.Configuration;
using vos.Service.Forage.Services;
using vos.Service.Shared;
using vos.Service.Shared.DagNode;
using vos.Service.Shared.Hosting;
using vos.Service.Shared.Subscriptions;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var launchSettings = ForageLaunchSettings.Parse(args, builder.Configuration);
if (launchSettings == null)
{
    Console.WriteLine(ForageLaunchSettings.UsageMessage);
    Environment.Exit(1);
    return;
}

var servicePort = launchSettings.Service.Port;
var myceliumUrl = launchSettings.Service.MyceliumUrl;
var serviceToken = launchSettings.Service.Token;
var apiKey = launchSettings.Service.ApiKey;
var verificationKey = launchSettings.Service.VerificationKey;

var isTestingEnv = builder.Environment.IsEnvironment("Testing");
ServiceHost.ConfigureLogging("Forage", "forage-.log", writeToFile: !isTestingEnv);

try
{
    Log.Information(
        "VillageOS Forage Service - Port: {Port}, Mycelium: {MyceliumUrl}, fetching through {Subdomain}",
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
    builder.Services.AddSingleton<IDiscoveryRunStarter, DiscoveryRunStarter>();

    var app = builder.Build();

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMyceliumModelToken();
    }

    // A run is started by the site entering the state the discovery connection watches, so the body is
    // whatever the broker posts for a dispatch and the subject is read through the shared classifier
    // rather than a shape of this service's own.
    var handleEndpoint = app.MapPost("/handle", async (
        CoveringSourceService coveringSources,
        DiscoveryRunner runner,
        AnalysisSpawner analysis,
        IDiscoveryRunStarter starter,
        HttpContext httpContext) =>
    {
        using var reader = new StreamReader(httpContext.Request.Body);
        var request = HandleRequestRouter.Classify(await reader.ReadToEndAsync());

        if (request.Kind != HandleRequestKind.RelationshipSubject)
            return Results.BadRequest(new { error = HandleRequestRouter.DescribeExpectedShapes("Forage") });

        var siteId = request.SubjectId;
        starter.Start(token => Discover(siteId, coveringSources, runner, analysis, token));

        // Accepted, not done — see IDiscoveryRunStarter for what closes it. `success` is the broker's
        // own contract: a body declaring it false is a failed dispatch whatever the status said.
        return Results.Accepted(value: new { success = true, siteId });
    });
    if (authEnabled) handleEndpoint.RequireAuthorization();

    app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "Forage" }));

    var shutdownEndpoint = app.MapPost("/shutdown", (IHostApplicationLifetime lifetime) =>
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(300);
            lifetime.StopApplication();
        });

        return Results.Ok(new { message = "Shutting down Forage" });
    });
    if (authEnabled) shutdownEndpoint.RequireAuthorization();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Forage terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// One run. Nothing returns from here, so what it found is reported to the log.
static async Task Discover(
    Guid siteId,
    CoveringSourceService coveringSources,
    DiscoveryRunner runner,
    AnalysisSpawner analysis,
    CancellationToken cancellationToken)
{
    var coverage = await coveringSources.ForSiteAsync(siteId, cancellationToken);
    if (coverage == null)
    {
        // Nothing is written and the analysis is not started, so the site stays in the state that
        // dispatched this and the run is driven again. Carrying on would report that no source covers
        // the site, which is the answer an unreachable gateway must never be mistaken for.
        Log.Error("Could not read which sources cover site {SiteId}; leaving the run outstanding rather " +
                  "than reporting that none do", siteId);
        return;
    }

    var report = await runner.RunAsync(siteId, coverage.Covering, coverage.Values, cancellationToken);
    foreach (var outcome in report.Unresolved)
        Log.Warning("Source {Source} left site {SiteId} undiscovered: {Reason}",
            outcome.Source, siteId, outcome.Reason);

    // Whatever mixture resolved, including none. The analysis reports against what discovery left it, and
    // a site whose sources were all unavailable is exactly the case a planner needs the analysis to say
    // something about rather than silently never running.
    var spawn = await analysis.SpawnAsync(siteId, coverage.Analysis, cancellationToken);
    if (!spawn.Started)
        Log.Information("No analysis started for site {SiteId}: {Reason}", siteId, spawn.Reason);
}

// Exposed to WebApplicationFactory<Program> in the test project per docs/SERVICES.md.
public partial class Program { }
