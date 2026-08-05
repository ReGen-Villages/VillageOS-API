using vos.Auth.Shared;
using vos.Service.ModelBridge.Services;
using vos.Service.Shared;
using vos.Service.Shared.Configuration;
using vos.Service.Shared.DagNode;
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

var logPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "logs", "model-bridge-.log");
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "ModelBridge")
    .WriteTo.File(
        path: logPath,
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
        shared: true)
    .CreateLogger();

try
{
    Log.Information("VillageOS ModelBridge Service — Port: {Port}, Mycelium: {MyceliumUrl}", servicePort, myceliumUrl);

    builder.Host.UseSerilog();
    builder.WebHost.UseUrls($"http://localhost:{servicePort}");
    builder.Services.AddHttpClient();

    var authEnabled = !string.IsNullOrEmpty(signingKey);
    if (authEnabled)
    {
        builder.AddMyceliumTokenAuth(signingKey!, issuer: launchSettings.Issuer, audience: launchSettings.Audience);
        Log.Information("JWT authentication enabled (issuer={Issuer}, audience={Audience})", launchSettings.Issuer, launchSettings.Audience);
    }

    builder.Services.AddSingleton(sp =>
        new EndpointServiceMyceliumClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<EndpointServiceMyceliumClient>>(),
            "ModelBridge",
            myceliumUrl,
            serviceToken));

    builder.Services.AddSingleton(sp =>
        new ModelBridgeNode(sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<ModelBridgeNode>>(), myceliumUrl, serviceToken));

    var app = builder.Build();

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMyceliumRequestToken();
    }

    app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () =>
    {
        try
        {
            var registered = await app.Services.GetRequiredService<EndpointServiceMyceliumClient>().RegisterAsync(servicePort);
            Log.Information("ModelBridge service {Status} with mycelium", registered ? "registered" : "failed to register");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during ModelBridge startup registration");
        }
    }));

    app.Lifetime.ApplicationStopping.Register(() => _ = Task.Run(async () =>
    {
        try { await app.Services.GetRequiredService<EndpointServiceMyceliumClient>().DeregisterAsync(); }
        catch (Exception ex) { Log.Error(ex, "Error during ModelBridge shutdown deregistration"); }
    }));

    var handle = app.MapPost("/handle", async (HttpContext ctx, ModelBridgeNode node) =>
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var root = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(await reader.ReadToEndAsync());
        if (DagNodeService.IsNodeEnvelope(root))
            return Results.Ok(await node.HandleNodeAsync(root, ctx.RequestAborted));
        return Results.BadRequest(new { error = "ModelBridge is a DAG node; expected a node envelope (runId, nodeId)." });
    });
    if (authEnabled) handle.RequireAuthorization();

    var manifest = app.MapGet("/manifest", (ModelBridgeNode node) => Results.Ok(node.Ports));
    if (authEnabled) manifest.RequireAuthorization();

    app.MapGet("/health", () => new { status = "Healthy", service = "ModelBridge" });
    app.MapGet("/stats", (EndpointServiceMyceliumClient client) => new { service = "ModelBridge", version = "1.0.0", handlerId = client.HandlerId.ToString(), myceliumUrl });

    var shutdown = app.MapPost("/shutdown", (IHostApplicationLifetime lifetime) =>
    {
        _ = Task.Run(async () => { await Task.Delay(300); lifetime.StopApplication(); });
        return new { message = "Shutting down ModelBridge service" };
    });
    if (authEnabled) shutdown.RequireAuthorization();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "ModelBridge service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
