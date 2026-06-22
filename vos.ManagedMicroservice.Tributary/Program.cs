using System.Globalization;
using System.Text.Json;
using vos.Auth.Shared;
using vos.ManagedMicroservice.Tributary.Configuration;
using vos.ManagedMicroservice.Tributary.Helpers;
using vos.ManagedMicroservice.Tributary.Models;
using vos.ManagedMicroservice.Tributary.Services;
using vos.ManagedMicroservice.Shared.Validation;
using vos.ManagedMicroservice.Shared.DagNode;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var cliArgs = CliArgs.Parse(args, builder.Configuration);
if (cliArgs == null)
{
    Console.WriteLine(CliArgs.UsageMessage);
    Environment.Exit(1);
    return;
}

var servicePort = cliArgs.Port;
var myceliumUrl = cliArgs.MyceliumUrl;
var serviceToken = cliArgs.Token;
var signingKey = cliArgs.SigningKey;

// Skip the file sink when running under WebApplicationFactory<Program> tests. Same
// rationale as Metabolism — file I/O under the test host has no value and invites flakiness.
var isTestingEnv = builder.Environment.IsEnvironment("Testing");

var loggerConfig = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "Tributary");

if (!isTestingEnv)
{
    var logPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "logs", "tributary-.log");
    loggerConfig = loggerConfig.WriteTo.File(
        path: logPath,
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
        shared: true);
}

Log.Logger = loggerConfig.CreateLogger();

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
            issuer: cliArgs.Issuer,
            audience: cliArgs.Audience);
        Log.Information("JWT authentication enabled for incoming mycelium requests (issuer={Issuer}, audience={Audience})",
            cliArgs.Issuer, cliArgs.Audience);
    }

    builder.Services.AddSingleton(sp =>
        new MyceliumClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<MyceliumClient>>(),
            myceliumUrl,
            serviceToken));
    builder.Services.AddSingleton<IEndpointMyceliumClient>(sp => sp.GetRequiredService<MyceliumClient>());
    builder.Services.AddSingleton<ObservationIngestService>();
    // Per-process token-exchange cache (Task #5470). TimeProvider.System drives its refresh threshold;
    // tests substitute a fake clock. Singleton so the cache survives across /handle requests.
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<TokenExchangeCache>();
    builder.Services.AddSingleton<EndpointCallService>();
    builder.Services.AddSingleton(sp =>
        new TributaryNode(
            sp.GetRequiredService<EndpointCallService>(),
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<TributaryNode>>(),
            myceliumUrl,
            serviceToken));

    var app = builder.Build();

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }

    var handleEndpoint = app.MapPost("/handle", async (
        HttpContext httpContext,
        EndpointCallService endpointCallService,
        TributaryNode node) =>
    {
        JsonDocument doc;
        try
        {
            doc = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: httpContext.RequestAborted);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "Request body must be valid JSON." });
        }

        using (doc)
        {
            var root = doc.RootElement;

            // Additive DAG-node path (Feature #5628): an orchestrator invocation carries runId+nodeId.
            // Every other body is a normal endpoint call and runs the identical EndpointCallService path.
            if (DagNodeService.IsNodeEnvelope(root))
                return Results.Ok(await node.HandleNodeAsync(root, httpContext.RequestAborted));

            var request = root.Deserialize<EndpointCallRequest>(WebJsonOptions) ?? new EndpointCallRequest();
            var result = await endpointCallService.ExecuteAsync(request, httpContext.RequestAborted);
            return ToHttpResult(result);
        }
    });
    if (authEnabled) handleEndpoint.RequireAuthorization();

    // Advertise the node's ports for the orchestrator and the Trellis palette.
    var manifestEndpoint = app.MapGet("/manifest", (TributaryNode node) => Results.Ok(node.Ports));
    if (authEnabled) manifestEndpoint.RequireAuthorization();

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

// Exposed to WebApplicationFactory<Program> in the test project per docs/MICROSERVICES.md.
// Top-level statements compile to a `Program` class that is internal by default — this empty
// partial declaration just elevates it to public so the test factory can name it.
public partial class Program
{
    // Match the minimal-API model-binder: case-insensitive, camelCase JSON.
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);
}
