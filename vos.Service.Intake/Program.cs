using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;
using vos.Service.Intake;
using vos.Service.Intake.Configuration;
using vos.Service.Intake.Models;
using vos.Service.Intake.Services;
using vos.Service.Shared;
using vos.Service.Shared.Hosting;

var builder = WebApplication.CreateBuilder(args);

var launchSettings = IntakeLaunchSettings.Parse(args, builder.Configuration);
var mailDelivery = MailDelivery.Parse(args, builder.Configuration);
if (launchSettings == null || mailDelivery == null)
{
    Console.WriteLine(IntakeLaunchSettings.UsageMessage);
    Environment.Exit(1);
    return 1;
}

if (mailDelivery.WhyRefusedIn(builder.Environment.EnvironmentName) is { } refusal)
{
    Console.WriteLine(refusal);
    Environment.Exit(1);
    return 1;
}

var publicFormOrigins = launchSettings.PublicFormOrigins;

var servicePort = launchSettings.Service.Port;
var myceliumUrl = launchSettings.Service.MyceliumUrl;
var launchToken = builder.Services.AddLaunchToken(launchSettings.Service.Token, myceliumUrl);
// Nothing launches this service, so no token is ever minted for it and none is refreshed when one
// expires. The key is the credential it runs on, and every client it reaches the broker with holds it.
var apiKey = launchSettings.Service.ApiKey;

var isTestingEnv = builder.Environment.IsEnvironment("Testing");
ServiceHost.ConfigureLogging("Intake", "intake-.log", launchSettings.Service.LogDirectory, writeToFile: !isTestingEnv);

try
{
    Log.Information("VillageOS Intake Service — Port: {Port}, Mycelium: {MyceliumUrl}", servicePort, myceliumUrl);

    builder.Host.UseSerilog();
    builder.WebHost.UseUrls($"http://localhost:{servicePort}");
    // A submission is read into memory whole, so the body is capped well below Kestrel's default, before
    // anything has looked at it.
    builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = SubmissionLimits.MaximumBodyBytes);
    builder.Services.AddHttpClient();

    // Every caller reaches this service through the reverse proxy, so the connection is from loopback and
    // the caller's own address is in the forwarded header. Without this a budget meant for one submitter
    // would be shared by everybody on the internet.
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
        options.KnownProxies.Clear();
        options.KnownProxies.Add(IPAddress.Loopback);
        options.KnownProxies.Add(IPAddress.IPv6Loopback);
    });

    builder.Services.AddRateLimiter(options =>
    {
        void PerSource(string policyName, int requestsAllowed, TimeSpan window) =>
            options.AddPolicy(policyName, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = requestsAllowed, Window = window }));
        PerSource(SubmissionRate.PolicyName, SubmissionRate.RequestsAllowed, SubmissionRate.Window);
        PerSource(TileRate.PolicyName, TileRate.RequestsAllowed, TileRate.Window);

        options.OnRejected = async (context, cancellation) =>
        {
            var comeBackIn = (int)Math.Ceiling(
                (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var after)
                    ? after
                    : SubmissionRate.Window).TotalSeconds);

            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.HttpContext.Response.Headers.RetryAfter = comeBackIn.ToString();
            IntakeEndpoints.LogRefusal(context.HttpContext, "the source has spent its budget");
            await context.HttpContext.Response.WriteAsJsonAsync(
                new Refusal(RefusalCode.TooManyRequests, $"Too many requests. Try again in {comeBackIn} seconds.",
                    Refusal.With(("seconds", comeBackIn))), cancellation);
        };
    });

    // The public form lives on the main hostname and this service answers on its own, so the browser
    // asks whether that origin may call it. Nothing configured means no cross-origin caller at all.
    if (publicFormOrigins.Length > 0)
    {
        builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
            .WithOrigins(publicFormOrigins)
            .WithMethods("GET", "POST", "DELETE")
            .WithHeaders("Content-Type", SubmissionTicket.HeaderName)
            // The renewed ticket travels back in a response header, which a cross-origin page cannot
            // read unless it is named here — unnamed, renewal would work everywhere but from the form.
            .WithExposedHeaders(SubmissionTicket.HeaderName)));
        Log.Information("Cross-origin submissions allowed from {Origins}", string.Join(", ", publicFormOrigins));
    }

    builder.Services.AddSingleton(sp =>
        new IntakeMyceliumClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<IntakeMyceliumClient>>(),
            myceliumUrl,
            launchToken,
            apiKey));

    builder.Services.AddSingleton<HeldDeclarations>();

    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddModelClock<IntakeMyceliumClient>("Intake");
    if (mailDelivery.Server is { } mailServer)
    {
        builder.Services.AddSingleton(mailServer);
        builder.Services.AddSingleton<IVerificationMailer, SmtpVerificationMailer>();
    }
    else
    {
        // Said once, loudly, at the one moment somebody is watching: every code this service issues from
        // here on is readable in its log, and no submitter has to receive one to submit.
        Log.Warning("Verification codes are being written to this log rather than sent. "
                    + "No address is verified, and any submission may name any address.");
        builder.Services.AddSingleton<IVerificationMailer, ConsoleVerificationMailer>();
    }
    builder.Services.AddSingleton<AddressVerification>();
    builder.Services.AddSingleton<SubmissionTicket>();
    builder.Services.AddSingleton<SubmissionIntakeService>();
    builder.Services.AddSingleton<SubmissionFindingsService>();
    builder.Services.AddSingleton(new DocumentStore(
        Path.IsPathRooted(launchSettings.DocumentDirectory)
            ? launchSettings.DocumentDirectory
            : Path.Combine(AppContext.BaseDirectory, launchSettings.DocumentDirectory)));
    builder.Services.AddSingleton<SharedDocumentService>();
    builder.Services.AddHostedService<DocumentReclaimService>();
    builder.Services.AddSingleton(provider => new BasemapTileService(
        provider.GetRequiredService<IntakeMyceliumClient>(),
        provider.GetRequiredService<HeldDeclarations>(),
        launchSettings.FetcherSubdomain));
    builder.Services.AddSingleton(provider => new PositionLookupService(
        provider.GetRequiredService<IntakeMyceliumClient>(),
        provider.GetRequiredService<HeldDeclarations>(),
        launchSettings.FetcherSubdomain,
        provider.GetRequiredService<ILogger<PositionLookupService>>(),
        provider.GetRequiredService<ModelClock>()));

    var app = builder.Build();

    app.UseForwardedHeaders();

    if (publicFormOrigins.Length > 0)
        app.UseCors();

    app.UseRateLimiter();

    app.MapIntakeEndpoints();

    app.MapHealth("Intake");

    app.Run();
}
catch (Exception error)
{
    return ServiceHost.FailedToStart("Intake", error);
}
finally
{
    Log.CloseAndFlush();
}

return 0;

public sealed record VerificationAsked(string? EmailAddress);

public sealed record VerificationAnswered(string? EmailAddress, string? Code);

public sealed record FindingsAsked(string? SubmissionId, string? EmailAddress);

// Exposed to WebApplicationFactory<Program> in the test project per docs/SERVICES.md.
public partial class Program { }
