using vos.Auth.Shared;
using vos.Service.CSharp.Echo.Services;
using vos.Service.Shared;
using vos.Service.Shared.Configuration;
using vos.Service.Shared.DagNode;
using vos.Service.Shared.Subscriptions;
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

builder.Host.UseSerilog();
builder.WebHost.UseUrls($"http://localhost:{servicePort}");
builder.Services.AddHttpClient();

// Bug #5391: issuer/audience must come from the CLI so validation matches what Mycelium signed.
var authEnabled = !string.IsNullOrEmpty(signingKey);
if (authEnabled)
{
    builder.AddMyceliumTokenAuth(
        signingKey!,
        issuer: launchSettings.Issuer,
        audience: launchSettings.Audience);
    Log.Information("JWT authentication enabled for incoming mycelium requests (issuer={Issuer}, audience={Audience})",
        launchSettings.Issuer, launchSettings.Audience);
}

var requestCount = 0;

builder.Services.AddSingleton(sp =>
    new MyceliumClient(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<ILogger<MyceliumClient>>(),
        myceliumUrl,
        serviceToken));

builder.Services.AddSingleton(sp =>
    new EchoNode(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<ILogger<EchoNode>>(),
        myceliumUrl,
        serviceToken));

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

var handleEndpoint = app.MapPost("/handle", async (HttpContext ctx, EchoNode node) =>
{
    var count = Interlocked.Increment(ref requestCount);

    using var reader = new StreamReader(ctx.Request.Body);
    var rawJson = await reader.ReadToEndAsync();
    var root = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(rawJson);

    // Additive DAG-node path: an orchestrator invocation carries runId+nodeId. Everything else
    // is a legacy echo request and is handled exactly as before.
    if (DagNodeService.IsNodeEnvelope(root))
    {
        Log.Information("Echo node invocation #{Count}", count);
        return Results.Ok(await node.HandleNodeAsync(root, ctx.RequestAborted));
    }

    Log.Information("Echo request #{Count} received", count);
    return Results.Ok(new
    {
        success = true,
        service = "echo",
        requestNumber = count,
        receivedBytes = rawJson.Length,
        echo = root
    });
});
if (authEnabled) handleEndpoint.RequireAuthorization();

// Advertise the node's ports so the orchestrator and the Trellis palette can type-check wires.
var manifestEndpoint = app.MapGet("/manifest", (EchoNode node) => Results.Ok(node.Ports));
if (authEnabled) manifestEndpoint.RequireAuthorization();

app.MapGet("/health", () => new
{
    status = "Healthy",
    service = "Echo",
    requestsProcessed = requestCount
});

app.MapGet("/stats", (MyceliumClient myceliumClient) => new
{
    service = "Echo",
    version = "1.0.0",
    requestsProcessed = requestCount,
    handlerId = myceliumClient.HandlerId.ToString(),
    myceliumUrl
});

// Write-kinds demo — POST { "thingId": "..." } (see WriteKindsDemo for prerequisites).
var writeKindsEndpoint = app.MapPost("/demo/write-kinds", async (WriteKindsDemoRequest req, MyceliumClient myceliumClient) =>
{
    var result = await new WriteKindsDemo(myceliumClient).RunAsync(req.ThingId, DateTime.UtcNow);
    return Results.Ok(result);
});
if (authEnabled) writeKindsEndpoint.RequireAuthorization();

// Selector demo — POST optional { "type": "...", "predicate": "..." } (defaults to Battery/powers).
var selectorEndpoint = app.MapPost("/demo/subscribe",
    async (SelectorDemoRequest? req, IHttpClientFactory httpFactory, ILoggerFactory loggerFactory) =>
{
    var subscriptions = new SubscriptionClient(
        httpFactory, loggerFactory.CreateLogger("SelectorDemo"), myceliumUrl, serviceToken);
    var selector = SelectorDemo.SliceByTypeAndTraverse(req?.Type ?? "Battery", req?.Predicate ?? "powers");
    var result = await new SelectorDemo(subscriptions).RunAsync(selector);
    return Results.Ok(result);
});
if (authEnabled) selectorEndpoint.RequireAuthorization();

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
