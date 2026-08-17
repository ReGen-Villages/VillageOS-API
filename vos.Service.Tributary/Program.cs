using System.Globalization;
using System.Text.Json;
using vos.Auth.Shared;
using vos.Service.Tributary.Helpers;
using vos.Service.Tributary.Models;
using vos.Service.Tributary.Services;
using vos.Service.Shared;
using vos.Service.Shared.Hosting;
using vos.Service.Shared.Configuration;
using vos.Service.Shared.Subscriptions;
using vos.Service.Shared.Validation;
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
ServiceHost.ConfigureLogging("Tributary", "tributary-.log", writeToFile: !isTestingEnv);

try
{
    Log.Information("VillageOS Tributary Service - Port: {Port}, Mycelium: {MyceliumUrl}", servicePort, myceliumUrl);

    builder.Host.UseSerilog();
    builder.WebHost.UseUrls($"http://localhost:{servicePort}");
    builder.Services.AddHttpClient();

    // Add JWT auth if mycelium provided a signing key. Bug #5391: use the
    // issuer/audience Mycelium passes via CLI so validation matches what
    // Mycelium signed.
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

    builder.Services.AddSingleton(sp =>
        new MyceliumClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<MyceliumClient>>(),
            myceliumUrl,
            serviceToken));
    builder.Services.AddSingleton<IEndpointMyceliumClient>(sp => sp.GetRequiredService<MyceliumClient>());
    // Reads which kinds an endpoint reaches. A scoped snapshot, not a property read, because a kind
    // is a Thing the endpoint relates to rather than a word it carries.
    builder.Services.AddSingleton<ISubscriptionClient>(sp =>
        new SubscriptionClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<SubscriptionClient>>(),
            myceliumUrl,
            serviceToken));
    builder.Services.AddSingleton<ObservationIngestService>();
    // Per-process token-exchange cache (Task #5470). TimeProvider.System drives its refresh threshold;
    // tests substitute a fake clock. Singleton so the cache survives across /handle requests.
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<TokenExchangeCache>();
    builder.Services.AddSingleton<EndpointCallService>();

    var app = builder.Build();

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMyceliumModelToken();
    }

    var handleEndpoint = app.MapPost("/handle", async (
        EndpointCallRequest request,
        EndpointCallService endpointCallService,
        HttpContext httpContext) =>
    {
        var result = await endpointCallService.ExecuteAsync(request, httpContext.RequestAborted);
        return ToHttpResult(result);
    });
    if (authEnabled) handleEndpoint.RequireAuthorization();

    app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "Tributary" }));

    var shutdownEndpoint = app.MapPost("/shutdown", (IHostApplicationLifetime lifetime) =>
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(300);
            lifetime.StopApplication();
        });

        return Results.Ok(new { message = "Shutting down Tributary" });
    });
    if (authEnabled) shutdownEndpoint.RequireAuthorization();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Tributary terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Map the framework-free EndpointCallResult back to the exact HTTP responses /handle always returned.
static IResult ToHttpResult(EndpointCallResult result)
{
    if (result.Error is ProblemError problem)
        return Results.Problem(detail: problem.Detail, statusCode: problem.StatusCode, title: problem.Title);
    if (result.Error is JsonError jsonError)
        return Results.Json(jsonError.Body, statusCode: jsonError.StatusCode);
    if (result.Ingest is { } ingest)
        return Results.Ok(new
        {
            success = true,
            endpointThingId = ingest.EndpointThingId,
            entitiesTouched = ingest.EntitiesTouched,
            observationsSubmitted = ingest.ObservationsSubmitted,
            message = "Readings ingested as observations on entity series."
        });
    return Results.Content(result.Content!, result.ContentType ?? "application/json");
}

// Exposed to WebApplicationFactory<Program> in the test project per docs/SERVICES.md.
// Top-level statements compile to a `Program` class that is internal by default — this empty
// partial declaration just elevates it to public so the test factory can name it.
public partial class Program { }
