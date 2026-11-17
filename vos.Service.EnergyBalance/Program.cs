using vos.Service.Auth;
using vos.Service.Shared;
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
    return 1;
}

var servicePort = launchSettings.Port;
var myceliumUrl = launchSettings.MyceliumUrl;
var launchToken = builder.Services.AddLaunchToken(launchSettings.Token, myceliumUrl);
var apiKey = launchSettings.ApiKey;
var verificationKey = launchSettings.VerificationKey;

ServiceHost.ConfigureLogging("EnergyBalance", "energy-balance-.log", launchSettings.LogDirectory);

try
{
    Log.Information("VillageOS EnergyBalance Service — Port: {Port}, Mycelium: {MyceliumUrl}", servicePort, myceliumUrl);

    builder.Host.UseSerilog();
    builder.WebHost.UseUrls($"http://localhost:{servicePort}");
    builder.Services.AddHttpClient();

    var authEnabled = builder.AddMyceliumTokenAuthWhenKeyed(verificationKey, launchSettings.Issuer, launchSettings.Audience);

    builder.Services.AddSingleton(sp =>
        new EndpointServiceMyceliumClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<EndpointServiceMyceliumClient>>(),
            "EnergyBalance",
            myceliumUrl,
            launchToken, apiKey: apiKey));

    builder.Services.AddSingleton(sp =>
        new EnergyBalanceNode(sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<EnergyBalanceNode>>(), myceliumUrl, launchToken, apiKey: apiKey));

    builder.Services.AddMyceliumRegistration("EnergyBalance", servicePort);

    var app = builder.Build();

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMyceliumModelToken();
    }

    var handle = app.MapPost("/handle", async (HttpContext ctx, EnergyBalanceNode node) =>
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var request = HandleRequestRouter.Classify(await reader.ReadToEndAsync());
        return request.Kind == HandleRequestKind.NodeEnvelope
            ? Results.Ok(await node.HandleNodeAsync(request.Json, ctx.RequestAborted))
            : Results.BadRequest(new { error = HandleRequestRouter.DescribeExpectedNodeEnvelope("EnergyBalance") });
    });
    if (authEnabled) handle.RequireAuthorization();

    var manifest = app.MapGet("/manifest", (EnergyBalanceNode node) => Results.Ok(node.Ports));
    if (authEnabled) manifest.RequireAuthorization();

    app.MapHealthAndStats("EnergyBalance", myceliumUrl);

    var shutdown = app.MapShutdown("EnergyBalance");
    if (authEnabled) shutdown.RequireAuthorization();

    app.Run();
}
catch (Exception error)
{
    return ServiceHost.FailedToStart("EnergyBalance", error);
}
finally
{
    Log.CloseAndFlush();
}

return 0;
