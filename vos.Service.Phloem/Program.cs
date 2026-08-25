using System.Text.Json;
using vos.Auth.Shared;
using vos.Service.Shared;
using vos.Service.Shared.Hosting;
using vos.Service.Shared.Configuration;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Services;
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
var apiKey = launchSettings.ApiKey;
var verificationKey = launchSettings.VerificationKey;

var isTestingEnv = builder.Environment.IsEnvironment("Testing");
ServiceHost.ConfigureLogging("Phloem", "phloem-.log", writeToFile: !isTestingEnv);

try
{
    Log.Information("VillageOS Phloem Orchestrator — Port: {Port}, Mycelium: {MyceliumUrl}", servicePort, myceliumUrl);

    builder.Host.UseSerilog();
    builder.WebHost.UseUrls($"http://localhost:{servicePort}");
    builder.Services.AddHttpClient();

    var authEnabled = !string.IsNullOrEmpty(verificationKey);
    if (authEnabled)
    {
        builder.AddMyceliumTokenAuth(verificationKey!, issuer: launchSettings.Issuer, audience: launchSettings.Audience);
        Log.Information("JWT authentication enabled for incoming mycelium requests (issuer={Issuer}, audience={Audience})",
            launchSettings.Issuer, launchSettings.Audience);
    }

    builder.Services.AddSingleton(sp =>
        new MyceliumGateway(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<MyceliumGateway>>(),
            myceliumUrl,
            serviceToken, apiKey: apiKey));
    builder.Services.AddSingleton<IMyceliumGateway>(sp => sp.GetRequiredService<MyceliumGateway>());
    builder.Services.AddSingleton<PipelineExecutor>();

    var app = builder.Build();

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMyceliumModelToken();
    }

    app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () =>
    {
        try
        {
            var registered = await app.Services.GetRequiredService<MyceliumGateway>().RegisterAsync(servicePort);
            Log.Information("Phloem orchestrator {Status} with mycelium", registered ? "registered" : "failed to register");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during Phloem startup registration");
        }
    }));

    app.Lifetime.ApplicationStopping.Register(() => _ = Task.Run(async () =>
    {
        try { await app.Services.GetRequiredService<MyceliumGateway>().DeregisterAsync(); }
        catch (Exception ex) { Log.Error(ex, "Error during Phloem shutdown deregistration"); }
    }));

    // Spawn-and-wait: { pipelineId, params? } -> run the DAG to completion -> return the result.
    var handleEndpoint = app.MapPost("/handle", async (HttpContext httpContext, PipelineExecutor executor) =>
    {
        JsonElement root;
        try
        {
            using var doc = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: httpContext.RequestAborted);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "Request body must be valid JSON." });
        }

        var trigger = SpawnTrigger.Resolve(root);
        switch (trigger.Kind)
        {
            case SpawnKind.Http when trigger.Async:
                // Async spawn — return the run id immediately and run the DAG in the background so the editor
                // animates over SSE rather than blocking. A detached token lets the run outlive the request.
                var asyncRunId = Guid.NewGuid();
                var asyncPipelineId = trigger.PipelineId;
                var asyncParams = trigger.Params;
                _ = Task.Run(async () =>
                {
                    try { await executor.RunAsync(asyncPipelineId, asyncParams, CancellationToken.None, asyncRunId); }
                    catch (Exception ex) { Log.Error(ex, "Async pipeline run {RunId} ({PipelineId}) failed", asyncRunId, asyncPipelineId); }
                });
                return Results.Ok(new { success = true, accepted = true, runId = asyncRunId, pipelineId = trigger.PipelineId });

            case SpawnKind.Http:
                // Synchronous spawn-and-wait — the caller (e.g. a programmatic client) blocks for the result.
                var result = await executor.RunAsync(trigger.PipelineId, trigger.Params, httpContext.RequestAborted);
                return Results.Ok(result);

            case SpawnKind.Graph:
                // A `X runs Pipeline` relationship trigger fires during a relationship-create and Mycelium
                // only waits ~15s — so ACK immediately and run the DAG in the background. The result lands on
                // the PipelineRun (animated over SSE in #5635). A detached token lets it outlive the request.
                var pipelineId = trigger.PipelineId;
                var runParams = trigger.Params;
                _ = Task.Run(async () =>
                {
                    try { await executor.RunAsync(pipelineId, runParams, CancellationToken.None); }
                    catch (Exception ex) { Log.Error(ex, "Graph-triggered pipeline run {PipelineId} failed", pipelineId); }
                });
                return Results.Ok(new { success = true, accepted = true, pipelineId = trigger.PipelineId });

            default:
                return Results.BadRequest(new { error = trigger.Error });
        }
    });
    if (authEnabled) handleEndpoint.RequireAuthorization();

    app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "Phloem" }));

    var shutdownEndpoint = app.MapPost("/shutdown", (IHostApplicationLifetime lifetime) =>
    {
        _ = Task.Run(async () => { await Task.Delay(300); lifetime.StopApplication(); });
        return Results.Ok(new { message = "Shutting down Phloem" });
    });
    if (authEnabled) shutdownEndpoint.RequireAuthorization();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Phloem orchestrator terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Exposed to WebApplicationFactory<Program> in the test project.
public partial class Program { }
