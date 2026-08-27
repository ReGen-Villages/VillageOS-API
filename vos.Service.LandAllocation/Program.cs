using vos.Auth.Shared;
using vos.Service.Shared;
using vos.Service.Shared.Subscriptions;
using vos.Service.Shared.Hosting;
using vos.Service.Shared.Configuration;
using vos.Service.LandAllocation.Services;
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

ServiceHost.ConfigureLogging("LandAllocation", "land-allocation-.log");

try
{
    Log.Information("VillageOS LandAllocation Service — Port: {Port}, Mycelium: {MyceliumUrl}", servicePort, myceliumUrl);

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
            "LandAllocation",
            myceliumUrl,
            serviceToken, apiKey: apiKey));

    builder.Services.AddSingleton(sp =>
        new LandAllocationReactiveHandler(sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<LandAllocationReactiveHandler>>(), myceliumUrl, serviceToken, apiKey: apiKey));

    // Recompute when an input moves. Unlike the balances, this service's inputs are not on the study it
    // writes to — the split lives on the allocations beside it — so the handler names those Things when
    // it watches (#6539), and re-names them on every recompute because a planner can add or remove one.
    builder.Services.AddInputChangeRecompute<LandAllocationReactiveHandler>(
        "LandAllocation", myceliumUrl, serviceToken, _ => LandAllocationReactiveHandler.InputProperties,
        (handler, studyId, ct) => handler.RecomputeAsync(studyId, ct));

    builder.Services.AddMyceliumRegistration("LandAllocation", servicePort);

    var app = builder.Build();

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMyceliumModelToken();
    }

    // Reactive only: this was built once, in the form the shipped analysis uses, so there is no node
    // path to route to (#6020).
    var handle = app.MapPost("/handle", async (HttpContext ctx, LandAllocationReactiveHandler reactive,
        InputChangeRecomputeService following) =>
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var request = HandleRequestRouter.Classify(await reader.ReadToEndAsync());

        if (request.Kind != HandleRequestKind.RelationshipSubject)
            return Results.BadRequest(new { error = HandleRequestRouter.DescribeExpectedShapes("LandAllocation") });

        var outputs = await reactive.RecomputeAsync(request.SubjectId, ctx.RequestAborted);
        // Registered after the read, because what this reads its inputs from is only known once it has.
        following.Watch(request.SubjectId, [.. reactive.ReadsFrom]);
        // A study reaching no parcel is answered rather than refused — there is nothing wrong with the
        // request — but the answer says which of the two it is, since "no outputs" alone reads the same
        // as a computation that produced none.
        return outputs is null
            ? Results.Ok(new { success = true, outputs, reason = "the study reaches no parcel, so neither footprint was worked out" })
            : Results.Ok(new { success = true, outputs });
    });
    if (authEnabled) handle.RequireAuthorization();

    app.MapHealthAndStats("LandAllocation", myceliumUrl);

    var shutdown = app.MapShutdown("LandAllocation");
    if (authEnabled) shutdown.RequireAuthorization();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "LandAllocation service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
