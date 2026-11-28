using System.Text.Json;
using vos.Service.Auth;
using vos.Service.Tributary.Models;
using vos.Service.Tributary.Services;
using vos.Service.Shared;
using vos.Service.Shared.Hosting;
using vos.Service.Shared.Configuration;
using vos.Service.Shared.DagNode;
using vos.Service.Shared.Subscriptions;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;
using Serilog;


var builder = WebApplication.CreateBuilder(args);

// Mycelium starts a service in its own working directory, which is where the default sources look, so
// the settings beside the executable are read here, below every other source.
builder.Configuration.Sources.Insert(0, new JsonConfigurationSource
{
    FileProvider = new PhysicalFileProvider(AppContext.BaseDirectory),
    Path = "appsettings.json",
    Optional = true,
});

var launchSettings = ServiceLaunchSettings.Parse(args, builder.Configuration);
if (launchSettings == null)
{
    Console.WriteLine(ServiceLaunchSettings.UsageMessage);
    Environment.Exit(1);
    return 1;
}

var servicePort = launchSettings.Port;
var myceliumUrl = launchSettings.MyceliumUrl;
var launchToken = builder.Services.AddLaunchToken(launchSettings.Token, myceliumUrl);
var apiKey = launchSettings.ApiKey;
var verificationKey = launchSettings.VerificationKey;

var isTestingEnv = builder.Environment.IsEnvironment("Testing");
ServiceHost.ConfigureLogging("Tributary", "tributary-.log", launchSettings.LogDirectory, writeToFile: launchSettings.LogToFile && !isTestingEnv);

try
{
    Log.Information("VillageOS Tributary Service - Port: {Port}, Mycelium: {MyceliumUrl}", servicePort, myceliumUrl);

    builder.Host.UseSerilog();
    builder.WebHost.UseUrls($"http://localhost:{servicePort}");
    builder.Services.AddHttpClient();

    var authEnabled = builder.AddMyceliumTokenAuthWhenKeyed(verificationKey, launchSettings.Issuer, launchSettings.Audience);

    builder.Services.AddSingleton(sp =>
        new MyceliumClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<MyceliumClient>>(),
            myceliumUrl,
            sp.GetRequiredService<TimeProvider>(),
            launchToken, apiKey: apiKey));
    builder.Services.AddSingleton<IEndpointMyceliumClient>(sp => sp.GetRequiredService<MyceliumClient>());
    // Reads which kinds an endpoint reaches. A scoped snapshot, not a property read, because a kind
    // is a Thing the endpoint relates to rather than a word it carries.
    builder.Services.AddSingleton<ISubscriptionClient>(sp =>
        new SubscriptionClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<SubscriptionClient>>(),
            myceliumUrl,
            launchToken, apiKey: apiKey));
    builder.Services.AddModelClock<MyceliumClient>("Tributary");
    builder.Services.AddSingleton<ObservationIngestService>();
    // The clock the token-exchange cache judges its refresh threshold by; tests substitute a fake one.
    builder.Services.AddSingleton(TimeProvider.System);
    // One per process, so the cache survives across /handle requests.
    builder.Services.AddSingleton<TokenExchangeCache>();
    // The DiskCache kind's store. One root for the process; entries live under one
    // directory per endpoint. The root is deployment configuration, not endpoint config.
    var cacheDirectory = builder.Configuration["CacheDirectory"]
        ?? Path.Combine(Directory.GetCurrentDirectory(), "cache");
    builder.Services.AddSingleton(sp =>
        new DiskResponseCache(cacheDirectory, sp.GetRequiredService<TimeProvider>()));
    builder.Services.AddSingleton(ProviderUnavailability.FromConfiguration(builder.Configuration));
    builder.Services.AddSingleton(sp => new PassedOnRequestLimit(
        launchSettings.PassedOnRequestLimit,
        sp.GetRequiredService<TimeProvider>()));
    builder.Services.AddSingleton<PassedOnRequestLimitWarnings>();
    builder.Services.AddSingleton<EndpointCallService>();
    builder.Services.AddSingleton(sp => new TributaryStep(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<ILogger<TributaryStep>>(),
        myceliumUrl,
        launchToken,
        apiKey,
        sp.GetRequiredService<EndpointCallService>(),
        sp.GetRequiredService<IEndpointMyceliumClient>(),
        sp.GetRequiredService<TimeProvider>()));

    var app = builder.Build();

    app.Services.GetRequiredService<PassedOnRequestLimitWarnings>().WarnWhenTooShortToAnswerFirst();

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMyceliumModelToken();
    }

    // The body is read here rather than bound, because a step request and a direct request share the route.
    var handleEndpoint = app.MapPost("/handle", async (
        EndpointCallService endpointCallService,
        PassedOnRequestLimit passedOnRequestLimit,
        TributaryStep step,
        HttpContext httpContext) =>
    {
        if (!httpContext.Request.HasJsonContentType())
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);

        using var reader = new StreamReader(httpContext.Request.Body);
        var body = HandleRequestRouter.Classify(await reader.ReadToEndAsync(httpContext.RequestAborted));
        using var deadline = passedOnRequestLimit.StartDeadline(httpContext.RequestAborted);
        if (body.Kind == HandleRequestKind.NodeEnvelope)
            return Results.Ok(await step.HandleNodeAsync(body.Json, deadline, httpContext.RequestAborted));
        if (DirectRequest(body) is not { } request)
            return Results.BadRequest();

        var result = await endpointCallService.ExecuteAsync(request, new TributaryRequestDeadline(deadline));
        if (result.RetryAfter is { } providersWait)
            httpContext.Response.Headers.RetryAfter = providersWait.ToString();
        return ToHttpResult(result);
    });
    if (authEnabled) handleEndpoint.RequireAuthorization();

    var manifestEndpoint = app.MapGet("/manifest", () => Results.Ok(TributaryStep.FetchPorts));
    if (authEnabled) manifestEndpoint.RequireAuthorization();
    var stepManifestEndpoint = app.MapGet("/manifest/{step}", (string step) =>
        TributaryStep.ManifestOf(step) is { } manifest ? Results.Ok(manifest) : Results.NotFound());
    if (authEnabled) stepManifestEndpoint.RequireAuthorization();

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
catch (Exception error)
{
    return ServiceHost.FailedToStart("Tributary", error);
}
finally
{
    Log.CloseAndFlush();
}

return 0;

static EndpointCallRequest? DirectRequest(HandleRequest body)
{
    if (!body.IsJson) return null;
    try
    {
        return body.Json.Deserialize<EndpointCallRequest>(JsonSerializerOptions.Web);
    }
    catch (JsonException)
    {
        return null;
    }
}

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
            written = ingest.Written,
            message = "Readings ingested as observations on entity series."
        });
    return Results.Content(result.Content!, result.ContentType ?? "application/json",
        statusCode: result.StatusCode);
}

// Exposed to WebApplicationFactory<Program> in the test project per docs/SERVICES.md.
// Top-level statements compile to a `Program` class that is internal by default — this empty
// partial declaration just elevates it to public so the test factory can name it.
public partial class Program { }
