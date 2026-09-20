using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using vos.Service.Intake.Services;
using vos.Tests.Shared;

namespace vos.Service.Intake.Tests;

// Settings reach the host through UseSetting: ServiceLaunchSettings.Parse falls back to those when no
// command-line flags are present, which is always the case under the test host. Every call the service
// makes to the model goes through the per-test handler.
public class IntakeWebApplicationFactory : WebApplicationFactory<Program>
{
    public Func<HttpRequestMessage, HttpResponseMessage> HandlerCallback { get; set; }
        = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    // Base64 of Mycelium's public signing key. This service checks no inbound credential, so
    // setting it changes nothing — which is what a test says out loud, because it is one of the settings
    // every service is launched with and could easily be believed to close the public route.
    public string? VerificationKey { get; set; }

    public string Issuer { get; set; } = "VillageOS";

    public string Audience { get; set; } = "intake-handler";

    // Origin(s) of the public form allowed to call this service across origins. Null leaves
    // every cross-origin caller refused, the default a public service must start from.
    public string? PublicFormOrigin { get; set; }

    // Time as the service reads it. A ticket is judged by how long ago it was issued, so a test
    // that let the real clock run could only say the elapsed time was small.
    public MovableClock Clock { get; } = new(new DateTimeOffset(2026, 8, 22, 9, 30, 0, TimeSpan.Zero));

    // What the service logged about submissions, so a test can say a line names the submission
    // and carries none of what was submitted.
    public CapturingLogger<SubmissionIntakeService> Log { get; } = new();

    // Whether a request arrives with an address on it. False is the service run with nothing in
    // front of it, where every caller is one source because there is nothing to tell them apart by.
    public bool ArrivesThroughAProxy { get; set; } = true;

    // The service JWT. A non-empty one short-circuits the token exchange, so a test about the
    // key the service holds sets this to null to make the exchange happen.
    public string? Token { get; set; } = "test-token";

    // The durable credential a service nobody launches has to hold. Null is a service given
    // none.
    public string? ApiKey { get; set; }

    // Where a verification code goes instead of a mail server, so a test can read back the code
    // a person would have read in their mail.
    public CapturingMailer Mailer { get; } = new();

    // Whether the service keeps whichever mailer it wired for itself. False substitutes
    // Mailer, which is what a test about the exchange wants; true is for a test about which
    // mailer the settings chose.
    public bool KeepsTheServicesOwnMailer { get; set; }

    // How the service is running. Testing unless a test is about something the environment
    // decides — writing codes to the console is allowed on a development machine and nowhere else. Named
    // as the host names it, and never Environment, which would shadow System.Environment
    // for everything in this class.
    public string EnvironmentName { get; set; } = "Testing";

    // Where codes go, or null to leave the service on its default of sending them.
    public string? MailDelivery { get; set; }

    // Where shared files are kept, or null for the service's default beside itself. A test about
    // files gives a folder of its own and removes it after.
    public string? DocumentDirectory { get; set; }

    // Whether the hourly pass that takes out the files of let-go submissions runs with the host.
    // A test that drives the pass by hand leaves it out, or the two would race over the same folders.
    public bool RunsTheReclaimPass { get; set; } = true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.UseSetting("Port", "5000");
        builder.UseSetting("MyceliumUrl", "http://localhost");
        // A non-empty token short-circuits the /api/auth/token round trip in MyceliumClientBase, which is
        // why a test about the key sets Token to null.
        if (Token != null)
            builder.UseSetting("Token", Token);
        if (ApiKey != null)
            builder.UseSetting("ApiKey", ApiKey);
        // The service refuses to start without these unless codes go to the console, so every test
        // supplies them; nothing here reaches a mail server, because the mailer below is what sends.
        if (DocumentDirectory is not null)
            builder.UseSetting("DocumentDirectory", DocumentDirectory);
        builder.UseSetting("MailHost", "smtp.example.test");
        builder.UseSetting("MailFrom", "intake@example.test");
        if (MailDelivery != null)
            builder.UseSetting("MailDelivery", MailDelivery);
        if (VerificationKey != null)
        {
            builder.UseSetting("VerificationKey", VerificationKey);
            builder.UseSetting("Issuer", Issuer);
            builder.UseSetting("Audience", Audience);
        }
        if (PublicFormOrigin != null)
            builder.UseSetting("PublicFormOrigin", PublicFormOrigin);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(
                new PerCallHttpClientFactory(new MockHttpMessageHandler(request => HandlerCallback(request))));
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<ILogger<SubmissionIntakeService>>(Log);
            if (!KeepsTheServicesOwnMailer)
            {
                services.RemoveAll<IVerificationMailer>();
                services.AddSingleton<IVerificationMailer>(Mailer);
            }
            if (ArrivesThroughAProxy)
                services.AddSingleton<IStartupFilter, ArrivingThroughTheProxy>();
            if (!RunsTheReclaimPass)
                services.Remove(services.Single(descriptor => descriptor.ImplementationType == typeof(DocumentReclaimService)));
        });
    }

    // The test host opens no socket, so a request arrives with no address on it and the service
    // reads every caller as one source. Behind the reverse proxy the connection is from loopback and the
    // caller's own address is in the forwarded header, which is what this reproduces.
    private sealed class ArrivingThroughTheProxy : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            builder =>
            {
                builder.Use(async (context, following) =>
                {
                    context.Connection.RemoteIpAddress = IPAddress.Loopback;
                    await following(context);
                });
                next(builder);
            };
    }
}

// Where a verification code goes with no mail server to send it. A test reads the code back
// because reading it out of a mailbox is the one step in the exchange it cannot take.
public sealed class CapturingMailer : IVerificationMailer
{
    private readonly List<(string EmailAddress, string Code)> _sent = [];

    // What the mail server does instead of accepting the message, where a test is about a
    // deployment whose mail is not working.
    public Exception? Refuses { get; set; }

    public IReadOnlyList<(string EmailAddress, string Code)> Sent => _sent;

    public string CodeSentTo(string emailAddress) =>
        _sent.Last(sent => sent.EmailAddress == emailAddress).Code;

    public Task SendAsync(string emailAddress, string code, CancellationToken cancellation)
    {
        if (Refuses is { } refusal) return Task.FromException(refusal);
        _sent.Add((emailAddress, code));
        return Task.CompletedTask;
    }
}

public sealed class MovableClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
