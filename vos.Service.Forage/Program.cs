using vos.Service.Auth;
using vos.Service.Forage.Configuration;
using vos.Service.Forage.Helpers;
using vos.Service.Forage.Services;
using vos.Service.Forage.Steps;
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
    return 1;
}

var servicePort = launchSettings.Service.Port;
var myceliumUrl = launchSettings.Service.MyceliumUrl;
var launchToken = builder.Services.AddLaunchToken(launchSettings.Service.Token, myceliumUrl);
var apiKey = launchSettings.Service.ApiKey;
var verificationKey = launchSettings.Service.VerificationKey;

var isTestingEnv = builder.Environment.IsEnvironment("Testing");
ServiceHost.ConfigureLogging("Forage", "forage-.log", launchSettings.Service.LogDirectory, writeToFile: !isTestingEnv);

try
{
    Log.Information(
        "VillageOS Forage Service - Port: {Port}, Mycelium: {MyceliumUrl}, looking up through {Subdomain}",
        servicePort, myceliumUrl, launchSettings.FetcherSubdomain);

    builder.Host.UseSerilog();
    builder.WebHost.UseUrls($"http://localhost:{servicePort}");
    builder.Services.AddHttpClient();

    var authEnabled = builder.AddMyceliumTokenAuthWhenKeyed(
        verificationKey, launchSettings.Service.Issuer, launchSettings.Service.Audience);

    // Coverage is relationships, so the only read this service makes is a scoped snapshot.
    builder.Services.AddSingleton<ISubscriptionClient>(sp =>
        new SubscriptionClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<SubscriptionClient>>(),
            myceliumUrl,
            launchToken, apiKey: apiKey));
    builder.Services.AddSingleton<CoveringSourceService>();
    builder.Services.AddSingleton<IEndpointBodyReader>(sp =>
        new EndpointBodyReader(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<EndpointBodyReader>>(),
            myceliumUrl,
            launchToken,
            launchSettings.FetcherSubdomain,
            launchSettings.SourceTimeout,
            apiKey));
    builder.Services.AddSingleton(sp =>
        new MyceliumRelationshipClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<MyceliumRelationshipClient>>(),
            myceliumUrl,
            launchToken, apiKey: apiKey));
    builder.Services.AddSingleton<ICoverageWriter>(sp => sp.GetRequiredService<MyceliumRelationshipClient>());
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddModelClock<MyceliumRelationshipClient>("Forage");
    builder.Services.AddSingleton<CoverageLedger>();
    builder.Services.AddSingleton<DivisionResolver>();
    builder.Services.AddSingleton<VocabularyEdgeWriter>();
    builder.Services.AddSingleton(sp => new ForageSteps(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<ILogger<ForageSteps>>(),
        myceliumUrl,
        launchToken,
        apiKey,
        sp.GetRequiredService<CoveringSourceService>(),
        sp.GetRequiredService<CoverageLedger>(),
        sp.GetRequiredService<DivisionResolver>(),
        sp.GetRequiredService<VocabularyEdgeWriter>(),
        launchSettings.SourceWindowDays));

    var app = builder.Build();

    launchSettings.WarnWhenTheSourceTimeoutIsNotLongerThanThePlatformsLimit(
        app.Services.GetRequiredService<ILogger<ForageLaunchSettings>>());

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMyceliumModelToken();
    }

    var handleEndpoint = app.MapPost("/handle", async (ForageSteps steps, HttpContext httpContext) =>
    {
        using var reader = new StreamReader(httpContext.Request.Body);
        var request = HandleRequestRouter.Classify(await reader.ReadToEndAsync());

        return request.Kind switch
        {
            HandleRequestKind.NodeEnvelope => Results.Ok(await steps.HandleNodeAsync(request.Json, httpContext.RequestAborted)),
            HandleRequestKind.RelationshipSubject => Results.BadRequest(new { success = false, error = ForageSteps.RelationshipCallRefusal }),
            _ => Results.BadRequest(new { error = HandleRequestRouter.DescribeExpectedShapes("Forage") }),
        };
    });
    if (authEnabled) handleEndpoint.RequireAuthorization();

    var manifestEndpoint = app.MapGet("/manifest/{step}", (string step) =>
        ForageSteps.ManifestOf(step) is { } manifest ? Results.Ok(manifest) : Results.NotFound());
    if (authEnabled) manifestEndpoint.RequireAuthorization();

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
catch (Exception error)
{
    return ServiceHost.FailedToStart("Forage", error);
}
finally
{
    Log.CloseAndFlush();
}

return 0;

// Exposed to WebApplicationFactory<Program> in the test project per docs/SERVICES.md.
public partial class Program { }
