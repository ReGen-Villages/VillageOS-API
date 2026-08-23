using vos.Auth.Shared;
using vos.Service.ModelBridge.Services;
using vos.Service.Shared;
using vos.Service.Shared.Hosting;
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
var verificationKey = launchSettings.VerificationKey;

ServiceHost.ConfigureLogging("ModelBridge", "model-bridge-.log");

try
{
    Log.Information("VillageOS ModelBridge Service — Port: {Port}, Mycelium: {MyceliumUrl}", servicePort, myceliumUrl);

    builder.Host.UseSerilog();
    builder.WebHost.UseUrls($"http://localhost:{servicePort}");
    builder.Services.AddHttpClient();

    var authEnabled = !string.IsNullOrEmpty(verificationKey);
    if (authEnabled)
    {
        builder.AddMyceliumTokenAuth(verificationKey!, issuer: launchSettings.Issuer, audience: launchSettings.Audience);
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

    builder.Services.AddMyceliumRegistration("ModelBridge", servicePort);

    var app = builder.Build();

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMyceliumModelToken();
    }

    var handle = app.MapPost("/handle", async (HttpContext ctx, ModelBridgeNode node) =>
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var root = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(await reader.ReadToEndAsync());
        if (DagNodeService.IsNodeEnvelope(root))
            return Results.Ok(await node.HandleNodeAsync(root, ctx.RequestAborted));
        return Results.BadRequest(new { error = HandleRequestRouter.DescribeExpectedNodeEnvelope("ModelBridge") });
    });
    if (authEnabled) handle.RequireAuthorization();

    var manifest = app.MapGet("/manifest", (ModelBridgeNode node) => Results.Ok(node.Ports));
    if (authEnabled) manifest.RequireAuthorization();

    app.MapHealthAndStats("ModelBridge", myceliumUrl);

    var shutdown = app.MapShutdown("ModelBridge");
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
