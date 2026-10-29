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
    return 1;
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
    builder.Services.AddModelClock<MyceliumGateway>("Phloem");
    builder.Services.AddSingleton<PipelineExecutor>();
    builder.Services.AddSingleton<PipelineStarter>();
    builder.Services.AddHostedService<CadenceKeeper>();

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
            var executor = app.Services.GetRequiredService<PipelineExecutor>();
            var placedAgain = await executor.RecoverWaitsAsync(CancellationToken.None);
            if (placedAgain > 0) Log.Information("Placed {Count} vigil(s) again for runs that were waiting", placedAgain);
            var carriedOn = await executor.RecoverTryingAsync(CancellationToken.None);
            if (carriedOn > 0) Log.Information("Carried {Count} run(s) on from a node that was trying", carriedOn);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during Phloem startup registration");
        }
    }));

    // Spawn-and-wait: { pipelineId, params? } -> run the DAG to completion -> return the result.
    var handleEndpoint = app.MapPost("/handle", async (HttpContext httpContext, PipelineExecutor executor, PipelineStarter starter) =>
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
        // The broker's name for this call in its request log. A call without one, or with one that is not an
        // identifier, still runs; the run simply records no request.
        var requestId = Guid.TryParse(httpContext.Request.Headers[RequestLogHeader].ToString(), out var named) ? named : (Guid?)null;
        RunSubject? heldSubject = null;
        if (trigger.Kind == SpawnKind.Http)
        {
            var reading = await starter.ReadSubjectAsync(trigger.Parameters, httpContext.RequestAborted);
            if (reading.Refusal is not null)
                return Results.BadRequest(new { error = reading.Refusal });
            heldSubject = reading.Subject;
        }

        switch (trigger.Kind)
        {
            case SpawnKind.Http when trigger.Async:
                // Async spawn — return the run id immediately and run the DAG in the background so the editor
                // animates over SSE rather than blocking. A detached token lets the run outlive the request.
                var asyncRunId = Guid.NewGuid();
                var asyncPipelineId = trigger.PipelineId;
                var asyncParameters = trigger.Parameters;
                var asyncRehearsal = trigger.Rehearsal;
                _ = Task.Run(async () =>
                {
                    try { await executor.RunAsync(asyncPipelineId, asyncParameters, CancellationToken.None, asyncRunId, subject: heldSubject, requestId: requestId, rehearsal: asyncRehearsal); }
                    catch (Exception ex) { Log.Error(ex, "Async pipeline run {RunId} ({PipelineId}) failed", asyncRunId, asyncPipelineId); }
                });
                return Results.Ok(new { success = true, accepted = true, runId = asyncRunId, pipelineId = trigger.PipelineId });

            case SpawnKind.Http:
                // Synchronous spawn-and-wait — the caller (e.g. a programmatic client) blocks for the result.
                var result = await executor.RunAsync(trigger.PipelineId, trigger.Parameters, httpContext.RequestAborted, subject: heldSubject, requestId: requestId, rehearsal: trigger.Rehearsal);
                return Results.Ok(result);

            case SpawnKind.Relationship when trigger.RelationshipId is { } written && await executor.IsStopAsync(written, httpContext.RequestAborted):
                // A stop request written along the orchestrator's own connection: to a run, or to a Thing whose runs
                // it stops. Acknowledged first, since a run under way stops only at its next step.
                if (trigger.Subject is not { } stopRequest)
                    return Results.BadRequest(new { error = "A stop names no stop request as its subject." });
                var stopping = trigger.TargetId;
                _ = Task.Run(async () =>
                {
                    try { await executor.StopAsync(stopRequest.Id, stopping, CancellationToken.None); }
                    catch (Exception ex) { Log.Error(ex, "Stopping the runs of {TargetId} failed", stopping); }
                });
                return Results.Ok(new { success = true, accepted = true, stops = stopping });

            case SpawnKind.Relationship when await executor.HoldsVigilAsync(trigger.TargetId, httpContext.RequestAborted):
                // The answer to a vigil a waiting node run holds comes as a state entry whose target is the vigil.
                // It is acknowledged before the run carries on, which can take longer than the broker waits.
                var vigilId = trigger.TargetId;
                _ = Task.Run(async () =>
                {
                    try { await executor.ResumeAsync(vigilId, CancellationToken.None); }
                    catch (Exception ex) { Log.Error(ex, "Resuming the run waiting on vigil {VigilId} failed", vigilId); }
                });
                return Results.Ok(new { success = true, accepted = true, resumes = vigilId });

            case SpawnKind.Relationship:
                // A relationship the broker sends — a `X runs Pipeline` write or a Thing entering a watched state —
                // fires while Mycelium waits ~15s, so the pipeline is resolved and the run acknowledged before
                // the DAG runs in the background. The result lands on the PipelineRun (animated over SSE). A
                // detached token lets it outlive the request. A start that fits no pipeline, or fits two equally,
                // is refused with 400: the broker records that as refused rather than failed, and nothing retries it.
                var start = await starter.ResolveAsync(trigger.TargetId, trigger.RelationshipId, trigger.Subject?.Id, httpContext.RequestAborted);
                if (!start.Started)
                    return Results.BadRequest(new { error = start.Refusal });

                var pipelineId = start.PipelineId!.Value;
                var runParameters = trigger.Parameters;
                var subject = trigger.Subject;
                _ = Task.Run(async () =>
                {
                    try { await executor.RunAsync(pipelineId, runParameters,CancellationToken.None, subject: subject, requestId: requestId); }
                    catch (Exception ex) { Log.Error(ex, "Pipeline run {PipelineId} started by the broker failed", pipelineId); }
                });
                return Results.Ok(new { success = true, accepted = true, pipelineId });

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
catch (Exception error)
{
    return ServiceHost.FailedToStart("Phloem", error);
}
finally
{
    Log.CloseAndFlush();
}

return 0;

// Exposed to WebApplicationFactory<Program> in the test project.
public partial class Program
{
    // The header the broker names each call's request-log entry in.
    internal const string RequestLogHeader = "Vos-Request-Id";
}
