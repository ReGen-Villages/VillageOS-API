using vos.Auth.Shared;
using vos.Service.Shared.Configuration;
using vos.Service.WaterReserve.Services;
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

var logPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "logs", "water-reserve-.log");
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "WaterReserve")
    .WriteTo.File(
        path: logPath,
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
        shared: true)
    .CreateLogger();

try
{
    Log.Information("VillageOS WaterReserve Service — Port: {Port}, Mycelium: {MyceliumUrl}", servicePort, myceliumUrl);

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
        new MyceliumClient(sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<MyceliumClient>>(), myceliumUrl, serviceToken));

    builder.Services.AddSingleton(sp =>
        new WaterReserveNode(sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<WaterReserveNode>>(), myceliumUrl, serviceToken));

    builder.Services.AddSingleton(sp =>
        new WaterReserveReactiveHandler(sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<WaterReserveReactiveHandler>>(), myceliumUrl, serviceToken));

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
            var registered = await app.Services.GetRequiredService<MyceliumClient>().RegisterAsync(servicePort);
            Log.Information("WaterReserve service {Status} with mycelium", registered ? "registered" : "failed to register");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during WaterReserve startup registration");
        }
    }));

    app.Lifetime.ApplicationStopping.Register(() => _ = Task.Run(async () =>
    {
        try { await app.Services.GetRequiredService<MyceliumClient>().DeregisterAsync(); }
        catch (Exception ex) { Log.Error(ex, "Error during WaterReserve shutdown deregistration"); }
    }));

    var handle = app.MapPost("/handle", async (HttpContext ctx, WaterReserveNode node, WaterReserveReactiveHandler reactive) =>
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var root = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(await reader.ReadToEndAsync());

        // A node envelope runs the DAG-node path; otherwise it's a graph relationship whose subject is the site
        // study — read its inputs, compute, and write the outputs back (the reactive/model-driven path, #5839).
        if (DagNodeService.IsNodeEnvelope(root))
            return Results.Ok(await node.HandleNodeAsync(root, ctx.RequestAborted));

        if (TrySubjectId(root, out var studyId))
        {
            var outputs = await reactive.RecomputeAsync(studyId, ctx.RequestAborted);
            return Results.Ok(new { success = true, outputs });
        }
        return Results.BadRequest(new { error = "WaterReserve expects a node envelope (runId, nodeId) or a graph relationship (subjectId)." });
    });
    if (authEnabled) handle.RequireAuthorization();

    static bool TrySubjectId(System.Text.Json.JsonElement root, out Guid id)
    {
        id = Guid.Empty;
        if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
        foreach (var p in root.EnumerateObject())
            if (string.Equals(p.Name, "subjectId", StringComparison.OrdinalIgnoreCase) && p.Value.TryGetGuid(out id))
                return true;
        return false;
    }

    var manifest = app.MapGet("/manifest", (WaterReserveNode node) => Results.Ok(node.Ports));
    if (authEnabled) manifest.RequireAuthorization();

    app.MapGet("/health", () => new { status = "Healthy", service = "WaterReserve" });
    app.MapGet("/stats", (MyceliumClient client) => new { service = "WaterReserve", version = "1.0.0", handlerId = client.HandlerId.ToString(), myceliumUrl });

    var shutdown = app.MapPost("/shutdown", (IHostApplicationLifetime lifetime) =>
    {
        _ = Task.Run(async () => { await Task.Delay(300); lifetime.StopApplication(); });
        return new { message = "Shutting down WaterReserve service" };
    });
    if (authEnabled) shutdown.RequireAuthorization();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "WaterReserve service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
