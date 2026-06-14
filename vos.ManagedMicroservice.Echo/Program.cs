using vos.Auth.Shared;
using vos.ManagedMicroservice.Echo.Configuration;
using vos.ManagedMicroservice.Echo.Services;
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

// Configure Serilog
var logPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "logs", "echo-.log");
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "Echo")
    .WriteTo.File(
        path: logPath,
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
        shared: true)
    .CreateLogger();

try
{

Log.Information("VillageOS Echo Endpoint Service — Port: {Port}, Mycelium: {MyceliumUrl}", servicePort, myceliumUrl);

var builder = WebApplication.CreateBuilder(args);
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
        issuer: cliArgs.Issuer ?? "VillageOS",
        audience: cliArgs.Audience ?? "VosClients");
    Log.Information("JWT authentication enabled for incoming mycelium requests (issuer={Issuer}, audience={Audience})",
        cliArgs.Issuer ?? "VillageOS", cliArgs.Audience ?? "VosClients");
}

var requestCount = 0;

builder.Services.AddSingleton(sp =>
    new MyceliumClient(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<ILogger<MyceliumClient>>(),
        myceliumUrl,
        serviceToken));

var app = builder.Build();

if (authEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

// Register with mycelium on startup
app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () =>
{
    try
    {
        var myceliumClient = app.Services.GetRequiredService<MyceliumClient>();
        var registered = await myceliumClient.RegisterAsync(servicePort);
        Log.Information("Echo endpoint service {Status} with mycelium",
            registered ? "registered" : "failed to register");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error during Echo startup registration");
    }
}));

// Deregister from mycelium on shutdown
app.Lifetime.ApplicationStopping.Register(() => _ = Task.Run(async () =>
{
    try
    {
        Log.Information("Shutting down Echo endpoint service — processed {Count} request(s)", requestCount);
        var myceliumClient = app.Services.GetRequiredService<MyceliumClient>();
        await myceliumClient.DeregisterAsync();
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error during Echo shutdown deregistration");
    }
}));

// POST /handle — Receive JSON payload from mycelium, echo it back
var handleEndpoint = app.MapPost("/handle", async (HttpContext ctx) =>
{
    var count = Interlocked.Increment(ref requestCount);

    Log.Information("Echo request #{Count} received", count);

    using var reader = new StreamReader(ctx.Request.Body);
    var rawJson = await reader.ReadToEndAsync();

    return Results.Ok(new
    {
        success = true,
        service = "echo",
        requestNumber = count,
        receivedBytes = rawJson.Length,
        echo = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(rawJson)
    });
});
if (authEnabled) handleEndpoint.RequireAuthorization();

// GET /health — Health check endpoint
app.MapGet("/health", () => new
{
    status = "Healthy",
    service = "Echo",
    requestsProcessed = requestCount
});

// GET /stats — Service statistics
app.MapGet("/stats", (MyceliumClient myceliumClient) => new
{
    service = "Echo",
    version = "1.0.0",
    requestsProcessed = requestCount,
    handlerId = myceliumClient.HandlerId.ToString(),
    myceliumUrl
});

// POST /shutdown — Graceful shutdown
var shutdownEndpoint = app.MapPost("/shutdown", (IHostApplicationLifetime lifetime) =>
{
    _ = Task.Run(async () =>
    {
        await Task.Delay(300);
        lifetime.StopApplication();
    });

    return new { message = "Shutting down Echo endpoint service" };
});
if (authEnabled) shutdownEndpoint.RequireAuthorization();

app.Run();

}
catch (Exception ex)
{
    Log.Fatal(ex, "Echo endpoint service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
