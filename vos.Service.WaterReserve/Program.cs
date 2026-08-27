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
var apiKey = launchSettings.ApiKey;
var verificationKey = launchSettings.VerificationKey;

ServiceHost.ConfigureLogging("WaterReserve", "water-reserve-.log");

try
{
    Log.Information("VillageOS WaterReserve Service — Port: {Port}, Mycelium: {MyceliumUrl}", servicePort, myceliumUrl);

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
            "WaterReserve",
            myceliumUrl,
            serviceToken, apiKey: apiKey));

    builder.Services.AddSingleton(sp =>
        new WaterReserveNode(sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<WaterReserveNode>>(), myceliumUrl, serviceToken, apiKey: apiKey));

    builder.Services.AddSingleton(sp =>
        new WaterReserveReactiveHandler(sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<WaterReserveReactiveHandler>>(), myceliumUrl, serviceToken, apiKey: apiKey));

    // Recompute when an input moves, so a study's result never presents a stale number as current.
    builder.Services.AddInputChangeRecompute<WaterReserveReactiveHandler>(
        "WaterReserve", myceliumUrl, serviceToken, _ => WaterReserveReactiveHandler.InputProperties,
        (handler, studyId, ct) => handler.RecomputeAsync(studyId, ct));

    builder.Services.AddMyceliumRegistration("WaterReserve", servicePort);

    var app = builder.Build();

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMyceliumModelToken();
    }

    var handle = app.MapPost("/handle", async (HttpContext ctx, WaterReserveNode node, WaterReserveReactiveHandler reactive,
        InputChangeRecomputeService following) =>
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var request = HandleRequestRouter.Classify(await reader.ReadToEndAsync());

        switch (request.Kind)
        {
            case HandleRequestKind.NodeEnvelope:
                return Results.Ok(await node.HandleNodeAsync(request.Json, ctx.RequestAborted));

            case HandleRequestKind.RelationshipSubject:
                following.Watch(request.SubjectId);
                var outputs = await reactive.RecomputeAsync(request.SubjectId, ctx.RequestAborted);
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
