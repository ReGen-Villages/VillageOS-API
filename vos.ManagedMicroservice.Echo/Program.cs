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
var brokerUrl = cliArgs.BrokerUrl;
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

Log.Information("Ducati Echo Endpoint Service — Port: {Port}, Broker: {BrokerUrl}", servicePort, brokerUrl);

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();
builder.WebHost.UseUrls($"http://localhost:{servicePort}");
builder.Services.AddHttpClient();

// Add JWT auth if broker provided a signing key
var authEnabled = !string.IsNullOrEmpty(signingKey);
if (authEnabled)
{
    builder.AddBrokerTokenAuth(signingKey!);
    Log.Information("JWT authentication enabled for incoming broker requests");
}

var requestCount = 0;

builder.Services.AddSingleton(sp =>
    new BrokerClient(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<ILogger<BrokerClient>>(),
        brokerUrl,
        serviceToken));

var app = builder.Build();

if (authEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

// Register with broker on startup
app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () =>
{
    try
    {
        var brokerClient = app.Services.GetRequiredService<BrokerClient>();
        var registered = await brokerClient.RegisterAsync(servicePort);
        Log.Information("Echo endpoint service {Status} with broker",
            registered ? "registered" : "failed to register");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error during Echo startup registration");
    }
}));

// Deregister from broker on shutdown
app.Lifetime.ApplicationStopping.Register(() => _ = Task.Run(async () =>
{
    try
    {
        Log.Information("Shutting down Echo endpoint service — processed {Count} request(s)", requestCount);
        var brokerClient = app.Services.GetRequiredService<BrokerClient>();
        await brokerClient.DeregisterAsync();
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error during Echo shutdown deregistration");
    }
}));

// POST /handle — Receive JSON payload from broker, echo it back
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
app.MapGet("/stats", (BrokerClient brokerClient) => new
{
    service = "Echo",
    version = "1.0.0",
    requestsProcessed = requestCount,
    handlerId = brokerClient.HandlerId.ToString(),
    brokerUrl
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
