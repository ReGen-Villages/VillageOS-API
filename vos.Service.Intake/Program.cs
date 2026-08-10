using Serilog;
using vos.Auth.Shared;
using vos.Service.Intake;
using vos.Service.Intake.Services;
using vos.Service.Shared.Configuration;
using vos.Service.Shared.Hosting;

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

var isTestingEnv = builder.Environment.IsEnvironment("Testing");
ServiceHost.ConfigureLogging("Intake", "intake-.log", writeToFile: !isTestingEnv);

try
{
    Log.Information("VillageOS Intake Service — Port: {Port}, Mycelium: {MyceliumUrl}", servicePort, myceliumUrl);

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
        new IntakeMyceliumClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<IntakeMyceliumClient>>(),
            myceliumUrl,
            serviceToken));

    builder.Services.AddSingleton<SubmissionIntakeService>();

    var app = builder.Build();

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }

    // The service does not register with Mycelium and is not reachable through the endpoint-forward route.
    // That route resolves where to forward from data in the model, so anything the model happens to name
    // would be within reach of whoever can call it. Submission stays a program of its own, holding its own
    // credential, so widening it later widens one service rather than every endpoint in the model.
    var submissions = app.MapPost("/submissions", async (HttpContext context, SubmissionIntakeService intake) =>
    {
        using var reader = new StreamReader(context.Request.Body);
        var document = await reader.ReadToEndAsync(context.RequestAborted);

        try
        {
            var accepted = await intake.SubmitAsync(document, context.RequestAborted);
            return Results.Ok(new { siteId = accepted.SiteId, studyId = accepted.StudyId, parcelId = accepted.ParcelId });
        }
        catch (SubmissionError error)
        {
            return Results.BadRequest(new { error = error.Message });
        }
    });
    if (authEnabled) submissions.RequireAuthorization();

    app.MapHealth("Intake");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Intake service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Exposed to WebApplicationFactory<Program> in the test project per docs/SERVICES.md.
public partial class Program { }
