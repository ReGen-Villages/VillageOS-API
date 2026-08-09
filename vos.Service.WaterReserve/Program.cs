using vos.Auth.Shared;
using vos.Service.Shared;
using vos.Service.Shared.Subscriptions;
using vos.Service.Shared.Hosting;
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

ServiceHost.ConfigureLogging("WaterReserve", "water-reserve-.log");

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
        new EndpointServiceMyceliumClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<EndpointServiceMyceliumClient>>(),
            "WaterReserve",
            myceliumUrl,
            serviceToken));

    builder.Services.AddSingleton(sp =>
        new WaterReserveNode(sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<WaterReserveNode>>(), myceliumUrl, serviceToken));

    builder.Services.AddSingleton(sp =>
        new WaterReserveReactiveHandler(sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<WaterReserveReactiveHandler>>(), myceliumUrl, serviceToken));

    builder.Services.AddSingleton(sp => new SubscriptionClient(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<ILogger<SubscriptionClient>>(), myceliumUrl, serviceToken));
    builder.Services.AddSingleton<ISubscriptionClient>(sp => sp.GetRequiredService<SubscriptionClient>());

    // Recompute when an input moves, so a study's result never presents a stale number as current.
    builder.Services.AddSingleton(sp => new InputChangeRecomputeService(
        sp.GetRequiredService<ISubscriptionClient>(),
        new RecomputeInputs("WaterReserve", WaterReserveReactiveHandler.InputProperties,
            (studyId, ct) => sp.GetRequiredService<WaterReserveReactiveHandler>().RecomputeAsync(studyId, ct)),
        sp.GetRequiredService<IHostEnvironment>(),
        sp.GetRequiredService<ILogger<InputChangeRecomputeService>>()));
    builder.Services.AddHostedService(sp => sp.GetRequiredService<InputChangeRecomputeService>());

    builder.Services.AddMyceliumRegistration("WaterReserve", servicePort);

    var app = builder.Build();

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }

    var handle = app.MapPost("/handle", async (HttpContext ctx, WaterReserveNode node, WaterReserveReactiveHandler reactive,
        InputChangeRecomputeService following) =>
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var root = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(await reader.ReadToEndAsync());

        switch (HandleRequestRouter.Classify(root, out var studyId))
        {
            case HandleRequestKind.NodeEnvelope:
                return Results.Ok(await node.HandleNodeAsync(root, ctx.RequestAborted));

            case HandleRequestKind.RelationshipSubject:
                following.Watch(studyId);
                var outputs = await reactive.RecomputeAsync(studyId, ctx.RequestAborted);
                return Results.Ok(new { success = true, outputs });

            default:
                return Results.BadRequest(new { error = HandleRequestRouter.DescribeExpectedShapes("WaterReserve") });
        }
    });
    if (authEnabled) handle.RequireAuthorization();

    var manifest = app.MapGet("/manifest", (WaterReserveNode node) => Results.Ok(node.Ports));
    if (authEnabled) manifest.RequireAuthorization();

    app.MapHealthAndStats("WaterReserve", myceliumUrl);

    var shutdown = app.MapShutdown("WaterReserve");
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
