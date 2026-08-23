using vos.Auth.Shared;
using vos.Service.Shared;
using vos.Service.Shared.Subscriptions;
using vos.Service.Shared.Hosting;
using vos.Service.Shared.Configuration;
using vos.Service.EnergyBalance.Services;
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

ServiceHost.ConfigureLogging("EnergyBalance", "energy-balance-.log");

try
{
    Log.Information("VillageOS EnergyBalance Service — Port: {Port}, Mycelium: {MyceliumUrl}", servicePort, myceliumUrl);

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
            "EnergyBalance",
            myceliumUrl,
            serviceToken));

    builder.Services.AddSingleton(sp =>
        new EnergyBalanceNode(sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<EnergyBalanceNode>>(), myceliumUrl, serviceToken));

    builder.Services.AddSingleton(sp =>
        new EnergyBalanceReactiveHandler(sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<EnergyBalanceReactiveHandler>>(), myceliumUrl, serviceToken));

    // Recompute when an input moves, so a study's result never presents a stale number as current.
    builder.Services.AddInputChangeRecompute<EnergyBalanceReactiveHandler>(
        "EnergyBalance", myceliumUrl, serviceToken, _ => EnergyBalanceReactiveHandler.InputProperties,
        (handler, studyId, ct) => handler.RecomputeAsync(studyId, ct));

    builder.Services.AddMyceliumRegistration("EnergyBalance", servicePort);

    var app = builder.Build();

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMyceliumModelToken();
    }

    var handle = app.MapPost("/handle", async (HttpContext ctx, EnergyBalanceNode node, EnergyBalanceReactiveHandler reactive,
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
                return Results.BadRequest(new { error = HandleRequestRouter.DescribeExpectedShapes("EnergyBalance") });
        }
    });
    if (authEnabled) handle.RequireAuthorization();

    var manifest = app.MapGet("/manifest", (EnergyBalanceNode node) => Results.Ok(node.Ports));
    if (authEnabled) manifest.RequireAuthorization();

    app.MapHealthAndStats("EnergyBalance", myceliumUrl);

    var shutdown = app.MapShutdown("EnergyBalance");
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
