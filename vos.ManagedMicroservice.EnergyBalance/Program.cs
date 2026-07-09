using vos.Auth.Shared;
using vos.ManagedMicroservice.EnergyBalance.Configuration;
using vos.ManagedMicroservice.EnergyBalance.Services;
using vos.ManagedMicroservice.Shared.DagNode;
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

var logPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "logs", "energy-balance-.log");
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "EnergyBalance")
    .WriteTo.File(
        path: logPath,
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
        shared: true)
    .CreateLogger();

try
{
    Log.Information("VillageOS EnergyBalance Service — Port: {Port}, Mycelium: {MyceliumUrl}", servicePort, myceliumUrl);

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();
    builder.WebHost.UseUrls($"http://localhost:{servicePort}");
    builder.Services.AddHttpClient();

    var authEnabled = !string.IsNullOrEmpty(signingKey);
    if (authEnabled)
    {
        builder.AddMyceliumTokenAuth(signingKey!, issuer: cliArgs.Issuer, audience: cliArgs.Audience);
        Log.Information("JWT authentication enabled (issuer={Issuer}, audience={Audience})", cliArgs.Issuer, cliArgs.Audience);
    }

    builder.Services.AddSingleton(sp =>
        new MyceliumClient(sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<MyceliumClient>>(), myceliumUrl, serviceToken));

    builder.Services.AddSingleton(sp =>
        new EnergyBalanceNode(sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<EnergyBalanceNode>>(), myceliumUrl, serviceToken));

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
            Log.Information("EnergyBalance service {Status} with mycelium", registered ? "registered" : "failed to register");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during EnergyBalance startup registration");
        }
    }));

    app.Lifetime.ApplicationStopping.Register(() => _ = Task.Run(async () =>
    {
        try { await app.Services.GetRequiredService<MyceliumClient>().DeregisterAsync(); }
        catch (Exception ex) { Log.Error(ex, "Error during EnergyBalance shutdown deregistration"); }
    }));

    var handle = app.MapPost("/handle", async (HttpContext ctx, EnergyBalanceNode node) =>
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var root = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(await reader.ReadToEndAsync());
        if (DagNodeService.IsNodeEnvelope(root))
            return Results.Ok(await node.HandleNodeAsync(root, ctx.RequestAborted));
        return Results.BadRequest(new { error = "EnergyBalance is a DAG node; expected a node envelope (runId, nodeId)." });
    });
    if (authEnabled) handle.RequireAuthorization();

    var manifest = app.MapGet("/manifest", (EnergyBalanceNode node) => Results.Ok(node.Ports));
    if (authEnabled) manifest.RequireAuthorization();

    app.MapGet("/health", () => new { status = "Healthy", service = "EnergyBalance" });
    app.MapGet("/stats", (MyceliumClient client) => new { service = "EnergyBalance", version = "1.0.0", handlerId = client.HandlerId.ToString(), myceliumUrl });

    var shutdown = app.MapPost("/shutdown", (IHostApplicationLifetime lifetime) =>
    {
        _ = Task.Run(async () => { await Task.Delay(300); lifetime.StopApplication(); });
        return new { message = "Shutting down EnergyBalance service" };
    });
    if (authEnabled) shutdown.RequireAuthorization();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "EnergyBalance service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
