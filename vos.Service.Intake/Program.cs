using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using vos.Service.Intake;
using vos.Service.Intake.Services;
using vos.Service.Shared.Configuration;
using vos.Service.Shared.Hosting;
using vos.Service.Shared.Subscriptions;

var builder = WebApplication.CreateBuilder(args);

var launchSettings = ServiceLaunchSettings.Parse(args, builder.Configuration);
var mailSettings = MailSettings.Parse(args, builder.Configuration);
if (launchSettings == null || mailSettings == null)
{
    Console.WriteLine(ServiceLaunchSettings.BuildUsageMessage(
        " [--publicFormOrigin=<origin>[,<origin>]] --mailHost=<host> --mailFrom=<address>",
        "\n  --publicFormOrigin  Origin(s) of the public form allowed to call this service across origins"
        + MailSettings.UsageMessage));
    Environment.Exit(1);
    return;
}

var publicFormOrigins = new LaunchSettingReader(args, builder.Configuration).Read("publicFormOrigin")
    ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

var servicePort = launchSettings.Port;
var myceliumUrl = launchSettings.MyceliumUrl;
var serviceToken = launchSettings.Token;
// Nothing launches this service, so no token is ever minted for it and none is refreshed when one
// expires. The key is the credential it runs on, and every client it reaches the broker with holds it.
var apiKey = launchSettings.ApiKey;

var isTestingEnv = builder.Environment.IsEnvironment("Testing");
ServiceHost.ConfigureLogging("Intake", "intake-.log", writeToFile: !isTestingEnv);

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
        options.AddPolicy(SubmissionRate.PolicyName, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = SubmissionRate.RequestsAllowed,
                    Window = SubmissionRate.Window,
                }));

        options.OnRejected = async (context, cancellation) =>
        {
            var comeBackIn = (int)Math.Ceiling(
                (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var after)
                    ? after
                    : SubmissionRate.Window).TotalSeconds);

            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.HttpContext.Response.Headers.RetryAfter = comeBackIn.ToString();
            LogRefusal(context.HttpContext, "the source has spent its budget");
            await context.HttpContext.Response.WriteAsJsonAsync(
                new { error = $"Too many requests. Try again in {comeBackIn} seconds." }, cancellation);
        };
    });

    // The public form lives on the main hostname and this service answers on its own, so the browser
    // asks whether that origin may call it. Nothing configured means no cross-origin caller at all.
    if (publicFormOrigins.Length > 0)
    {
        builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
            .WithOrigins(publicFormOrigins)
            .WithMethods("GET", "POST")
            .WithHeaders("Content-Type", SubmissionTicket.HeaderName)));
        Log.Information("Cross-origin submissions allowed from {Origins}", string.Join(", ", publicFormOrigins));
    }

    builder.Services.AddSingleton(sp =>
        new IntakeMyceliumClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<IntakeMyceliumClient>>(),
            myceliumUrl,
            serviceToken,
            apiKey));

    builder.Services.AddSingleton<ISubscriptionClient>(sp =>
        new SubscriptionClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<SubscriptionClient>>(),
            myceliumUrl,
            serviceToken,
            apiKey: apiKey));

    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton(mailSettings);
    builder.Services.AddSingleton<IVerificationMailer, SmtpVerificationMailer>();
    builder.Services.AddSingleton<AddressVerification>();
    builder.Services.AddSingleton<SubmissionTicket>();
    builder.Services.AddSingleton<SubmissionIntakeService>();

    var app = builder.Build();

    app.UseForwardedHeaders();

    if (publicFormOrigins.Length > 0)
        app.UseCors();

    app.UseRateLimiter();

    // The service does not register with Mycelium and is not reachable through the endpoint-forward route.
    // That route resolves where to forward from data in the model, so anything the model happens to name
    // would be within reach of whoever can call it. Submission stays a program of its own, holding its own
    // credential, so the anonymous route below is one service open rather than every endpoint in the model.
    //
    // Nothing here checks an inbound credential, and that is the decision rather than an omission: the
    // route takes a submission from someone who holds none. A verification key wired up but demanded
    // nowhere would read as protection and be none. What stands in its place is in AddressVerification,
    // SubmissionTicket, SubmissionRate and SubmissionLimits.

    // What a form draws itself with. The page asking holds no credential and so cannot read the model for
    // itself, and publishing that read would hand a stranger every Thing in it — so the service reads it
    // under its own credential and answers with the categories a submission may name and the imagery a map
    // may draw on, and nothing else.
    app.MapGet("/submissions/form", async (
        HttpContext context, ISubscriptionClient subscriptions, ILogger<SubmissionIntakeService> logger) =>
    {
        try
        {
            return Results.Ok(await subscriptions.ReadAsync(
                FormOptionsReader.Selector(), FormOptionsReader.Read, logger, context.RequestAborted));
        }
        // A model nobody seeded is a fault in the deployment rather than in the request, answered the way
        // a submission into one is: the caller is a stranger's browser and is told neither what is wrong
        // nor anything about the model it asked about.
        catch (Exception error) when (error is ModelNotSeededError or HttpRequestException
                                      || (error is TaskCanceledException
                                          && !context.RequestAborted.IsCancellationRequested))
        {
            logger.LogError(error, "A form could not be answered: {Reason}", error.Message);
            return Results.Problem("This service cannot answer at the moment.", statusCode: 503);
        }
    }).RequireRateLimiting(SubmissionRate.PolicyName);

    // Sending a code is the one thing this service does to somebody who did not ask for it, because the
    // address is a stranger's word for whose mailbox it is. Two things bound that: the rate limit on the
    // caller, and the budget one address has for codes.
    app.MapPost("/submissions/verification", async (
        HttpContext context,
        AddressVerification verification,
        IVerificationMailer mailer,
        ILogger<SubmissionIntakeService> logger) =>
    {
        var asked = await ReadAsync<VerificationAsked>(context);
        if (asked?.EmailAddress is not { } emailAddress)
            return Refused(context, "no address was given to verify",
                Results.BadRequest(new { error = "'emailAddress' is missing: there is nowhere to send a code." }));

        try
        {
            SubmissionLimits.EmailAddress(emailAddress, "emailAddress");
        }
        catch (SubmissionError error)
        {
            return Refused(context, error.Message, Results.BadRequest(new { error = error.Message }));
        }

        if (verification.CodeFor(emailAddress) is not { } code)
            return Refused(context, "the address has been sent as many codes as the window allows",
                Results.Json(
                    new { error = "That address has been sent as many codes as it can be for now." },
                    statusCode: StatusCodes.Status429TooManyRequests));

        try
        {
            await mailer.SendAsync(emailAddress, code, context.RequestAborted);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // The address is a stranger's, so a server refusing it says nothing about this deployment
            // being unwell — but neither this service nor the submitter can tell the two apart, and what
            // went wrong belongs where whoever runs the deployment reads it rather than in the answer.
            // The budget goes back, or three failures nobody saw would lock the address out for the hour.
            verification.NothingWasSent(emailAddress);
            logger.LogError(error, "A verification code could not be sent: {Reason}", error.Message);
            return Results.Problem("This service cannot send a code at the moment.", statusCode: 503);
        }

        return Results.Accepted();
    }).RequireRateLimiting(SubmissionRate.PolicyName);

    app.MapPost("/submissions/ticket", async (
        HttpContext context, AddressVerification verification, SubmissionTicket tickets) =>
    {
        var answered = await ReadAsync<VerificationAnswered>(context);
        if (answered?.EmailAddress is not { } emailAddress)
            return Refused(context, "no address was given with the code",
                Results.BadRequest(new { error = "'emailAddress' is missing: a ticket is issued against one." }));

        if (verification.WhyRefused(emailAddress, answered.Code) is { } wrongCode)
            return Refused(context, "the code was not accepted",
                Results.Json(new { error = wrongCode }, statusCode: StatusCodes.Status403Forbidden));

        return Results.Ok(new
        {
            ticket = tickets.Issue(emailAddress),
            validForSeconds = (int)SubmissionTicket.ValidFor.TotalSeconds,
        });
    }).RequireRateLimiting(SubmissionRate.PolicyName);

    app.MapPost("/submissions", async (
        HttpContext context,
        SubmissionIntakeService intake,
        SubmissionTicket tickets,
        ILogger<SubmissionIntakeService> logger) =>
    {
        if (context.Request.ContentLength > SubmissionLimits.MaximumBodyBytes)
            return Refused(context, "the body is beyond the cap",
                Results.StatusCode(StatusCodes.Status413PayloadTooLarge));

        var presented = context.Request.Headers[SubmissionTicket.HeaderName].ToString();
        if (tickets.WhyRefused(presented) is { } notFromAForm)
            return Refused(context, "the ticket was not accepted",
                Results.Json(new { error = notFromAForm }, statusCode: StatusCodes.Status403Forbidden));

        using var reader = new StreamReader(context.Request.Body);
        var document = await reader.ReadToEndAsync(context.RequestAborted);

        try
        {
            var submission = SubmissionReader.Read(document);

            // A submission naming no address falls through to the composer, which refuses it by name. A
            // ticket check first would answer a missing field with a rule about a ticket.
            if (submission.Contact?.EmailAddress?.Trim() is { Length: > 0 } claimed
                && !tickets.WasIssuedFor(presented, claimed))
                return Refused(context, "the ticket was issued against a different address",
                    Results.Json(
                        new { error = "This submission names an address that was not the one verified." },
                        statusCode: StatusCodes.Status403Forbidden));

            var accepted = await intake.SubmitAsync(submission, context.RequestAborted);
            logger.LogInformation("Submission {Reference} was written into the model", accepted.Reference);
            return Results.Ok(new { reference = accepted.Reference });
        }
        // Something whoever filled the form in can correct, and the message names the field rather than
        // quoting what was in it.
        catch (SubmissionError error)
        {
            return Refused(context, error.Message, Results.BadRequest(new { error = error.Message }));
        }
        // The submission was well formed and the deployment was not ready for it. Answering 400 would tell
        // whoever filled the form in to correct something they cannot reach, and this route takes anonymous
        // submissions, so what is wrong goes to the log rather than into the response.
        catch (Exception error) when (error is ModelNotSeededError or HttpRequestException
                                      || (error is TaskCanceledException
                                          && !context.RequestAborted.IsCancellationRequested))
        {
            logger.LogError(error, "A submission could not be accepted: {Reason}", error.Message);
            return Results.Problem("This service cannot accept submissions at the moment.", statusCode: 503);
        }
    }).RequireRateLimiting(SubmissionRate.PolicyName);

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

static IResult Refused(HttpContext context, string reason, IResult answer)
{
    LogRefusal(context, reason);
    return answer;
}

// The two verification bodies are an address and a code. Anything that does not read as one is nothing
// rather than an exception, and the route says which field is missing.
static async Task<T?> ReadAsync<T>(HttpContext context) where T : class
{
    if (context.Request.ContentLength > SubmissionLimits.MaximumVerificationBytes) return null;

    try { return await context.Request.ReadFromJsonAsync<T>(context.RequestAborted); }
    catch (JsonException) { return null; }
    catch (BadHttpRequestException) { return null; }
}

// A refusal is logged by why it was refused and by where it came from, and never by what was submitted.
// Contact details arrive on this route by design, and a log line is the one place they would leave the
// model behind — see docs/LAND_INTAKE.md §12.
static void LogRefusal(HttpContext context, string reason) =>
    context.RequestServices.GetRequiredService<ILogger<SubmissionIntakeService>>()
        .LogWarning("A submission was refused: {Reason}, source {Source}",
            reason, context.Connection.RemoteIpAddress?.ToString() ?? "unknown");

/// <summary>An address somebody wants a code sent to.</summary>
public sealed record VerificationAsked(string? EmailAddress);

/// <summary>The address and the code that was sent to it, exchanged for a ticket.</summary>
public sealed record VerificationAnswered(string? EmailAddress, string? Code);

// Exposed to WebApplicationFactory<Program> in the test project per docs/SERVICES.md.
public partial class Program { }
