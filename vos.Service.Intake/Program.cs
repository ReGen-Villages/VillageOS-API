using Serilog;
using vos.Auth.Shared;
using vos.Service.Intake;
using vos.Service.Intake.Services;
using vos.Service.Shared.Configuration;
using vos.Service.Shared.Hosting;
using vos.Service.Shared.Subscriptions;

var builder = WebApplication.CreateBuilder(args);

var launchSettings = ServiceLaunchSettings.Parse(args, builder.Configuration);
if (launchSettings == null)
{
    Console.WriteLine(ServiceLaunchSettings.BuildUsageMessage(
        " [--publicFormOrigin=<origin>[,<origin>]]",
        "\n  --publicFormOrigin  Origin(s) of the public form allowed to call this service across origins"));
    Environment.Exit(1);
    return;
}

var publicFormOrigins = new LaunchSettingReader(args, builder.Configuration).Read("publicFormOrigin")
    ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

var servicePort = launchSettings.Port;
var myceliumUrl = launchSettings.MyceliumUrl;
var serviceToken = launchSettings.Token;
var apiKey = launchSettings.ApiKey;
var verificationKey = launchSettings.VerificationKey;

var isTestingEnv = builder.Environment.IsEnvironment("Testing");
ServiceHost.ConfigureLogging("Intake", "intake-.log", writeToFile: !isTestingEnv);

try
{
    Log.Information("VillageOS Intake Service — Port: {Port}, Mycelium: {MyceliumUrl}", servicePort, myceliumUrl);

    builder.Host.UseSerilog();
    builder.WebHost.UseUrls($"http://localhost:{servicePort}");
    // A submission is read into memory whole, so the body is capped well below Kestrel's default. The cap
    // is not the rate limiting and bot checks a public endpoint needs (#6043) — it is the floor under them.
    builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = SubmissionSize.MaximumBytes);
    builder.Services.AddHttpClient();

    // The public form lives on the main hostname and this service answers on its own, so the browser
    // asks whether that origin may call it. Nothing configured means no cross-origin caller at all.
    if (publicFormOrigins.Length > 0)
    {
        builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
            .WithOrigins(publicFormOrigins)
            .WithMethods("POST")
            .WithHeaders("Content-Type")));
        Log.Information("Cross-origin submissions allowed from {Origins}", string.Join(", ", publicFormOrigins));
    }

    var authEnabled = !string.IsNullOrEmpty(verificationKey);
    if (authEnabled)
    {
        builder.AddMyceliumTokenAuth(verificationKey!, issuer: launchSettings.Issuer, audience: launchSettings.Audience);
        Log.Information("JWT authentication enabled (issuer={Issuer}, audience={Audience})", launchSettings.Issuer, launchSettings.Audience);
    }

    builder.Services.AddSingleton(sp =>
        new IntakeMyceliumClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<IntakeMyceliumClient>>(),
            myceliumUrl,
            serviceToken, apiKey: apiKey));

    builder.Services.AddSingleton<ISubscriptionClient>(sp =>
        new SubscriptionClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<SubscriptionClient>>(),
            myceliumUrl,
            serviceToken, apiKey: apiKey));

    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<SubmissionIntakeService>();

    var app = builder.Build();

    if (publicFormOrigins.Length > 0)
        app.UseCors();

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
        if (context.Request.ContentLength > SubmissionSize.MaximumBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

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
        // The submission was well formed and the model was not ready for it. Answering 400 would tell
        // whoever filled the form in to correct something they cannot reach, and this route is meant to
        // take anonymous submissions, so what is wrong goes to the log rather than into the response.
        catch (ModelNotSeededError error)
        {
            Log.Error(error, "A submission could not be accepted: {Reason}", error.Message);
            return Results.Problem("This service cannot accept submissions at the moment.", statusCode: 503);
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
