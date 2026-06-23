using System.Text.Json;
using vos.Auth.Shared;
using vos.ManagedMicroservice.Phloem.Configuration;
using vos.ManagedMicroservice.Phloem.Execution;
using vos.ManagedMicroservice.Phloem.Services;
using Serilog;

var cliArgs = CliArgs.Parse(args);
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

var builder = WebApplication.CreateBuilder(args);
var isTestingEnv = builder.Environment.IsEnvironment("Testing");

var loggerConfig = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "Phloem");

if (!isTestingEnv)
{
    var logPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "logs", "phloem-.log");
    loggerConfig = loggerConfig.WriteTo.File(
        path: logPath,
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
        shared: true);
}

Log.Logger = loggerConfig.CreateLogger();

try
{
    Log.Information("VillageOS Phloem Orchestrator — Port: {Port}, Mycelium: {MyceliumUrl}", servicePort, myceliumUrl);

    builder.Host.UseSerilog();
    builder.WebHost.UseUrls($"http://localhost:{servicePort}");
    builder.Services.AddHttpClient();

    var authEnabled = !string.IsNullOrEmpty(signingKey);
    if (authEnabled)
    {
        builder.AddMyceliumTokenAuth(signingKey!, issuer: cliArgs.Issuer, audience: cliArgs.Audience);
        Log.Information("JWT authentication enabled for incoming mycelium requests (issuer={Issuer}, audience={Audience})",
            cliArgs.Issuer, cliArgs.Audience);
    }

    // Archetype vocabulary pushed by Mycelium at launch (defaults when an arg is absent).
    builder.Services.AddSingleton(cliArgs.Model);
    builder.Services.AddSingleton(sp =>
        new MyceliumGateway(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<MyceliumGateway>>(),
            myceliumUrl,
            sp.GetRequiredService<PipelineModelOptions>(),
            serviceToken));
    builder.Services.AddSingleton<IMyceliumGateway>(sp => sp.GetRequiredService<MyceliumGateway>());
    builder.Services.AddSingleton<PipelineExecutor>();

    var app = builder.Build();

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
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
