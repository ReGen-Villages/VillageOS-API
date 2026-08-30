using vos.Auth.Shared;
using vos.Service.Shared;
using vos.Service.Shared.Subscriptions;
using vos.Service.Shared.Hosting;
using vos.Service.Shared.Configuration;
using vos.Service.RainwaterHarvest.Services;
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

ServiceHost.ConfigureLogging("RainwaterHarvest", "rainwater-harvest-.log");

try
{
    Log.Information("VillageOS RainwaterHarvest Service — Port: {Port}, Mycelium: {MyceliumUrl}", servicePort, myceliumUrl);

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
            "RainwaterHarvest",
            myceliumUrl,
            serviceToken, apiKey: apiKey));

    builder.Services.AddSingleton(sp =>
        new RainwaterHarvestReactiveHandler(sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<RainwaterHarvestReactiveHandler>>(), myceliumUrl, serviceToken, apiKey: apiKey));

    // Recompute when an input moves. Both footprints are among them, so a re-run of land allocation
    // carries through to this balance without anything dispatching it again.
    builder.Services.AddInputChangeRecompute<RainwaterHarvestReactiveHandler>(
        "RainwaterHarvest", myceliumUrl, serviceToken, handler => handler.WatchedProperties,
        (handler, studyId, ct) => handler.RecomputeAsync(studyId, ct));

    builder.Services.AddMyceliumRegistration("RainwaterHarvest", servicePort);

    var app = builder.Build();

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMyceliumModelToken();
    }

    // Reactive only: this was built once, in the form the shipped analysis uses, so there is no node
    // path to route to (#6020).
    var handle = app.MapPost("/handle", async (HttpContext ctx, RainwaterHarvestReactiveHandler reactive,
        InputChangeRecomputeService following) =>
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var request = HandleRequestRouter.Classify(await reader.ReadToEndAsync());

        if (request.Kind != HandleRequestKind.RelationshipSubject)
            return Results.BadRequest(new { error = HandleRequestRouter.DescribeExpectedShapes("RainwaterHarvest") });

        // Watched before the compute, not after: this balance is dispatched with the analysis, and the
        // footprints it reads are written later by a service on the layer below. The first compute
        // therefore finds nothing to work from on a study whose land has not been allocated yet, and the
        // watch registered here is what brings the balance back when those footprints arrive.
        following.Watch(request.SubjectId);
        var answer = await reactive.RecomputeAsync(request.SubjectId, ctx.RequestAborted);
        return Results.Ok(new { success = true, answer.Outputs, answer.WaitingFor });
    });
    if (authEnabled) handle.RequireAuthorization();

    app.MapHealthAndStats("RainwaterHarvest", myceliumUrl);

    var shutdown = app.MapShutdown("RainwaterHarvest");
    if (authEnabled) shutdown.RequireAuthorization();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "RainwaterHarvest service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
