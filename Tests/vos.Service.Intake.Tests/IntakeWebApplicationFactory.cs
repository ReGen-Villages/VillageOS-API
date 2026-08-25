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

    /// <summary>Base64 of Mycelium's public signing key. This service checks no inbound credential, so
    /// setting it changes nothing — which is what a test says out loud, because it is one of the settings
    /// every service is launched with and could easily be believed to close the public route.</summary>
    public string? VerificationKey { get; set; }

    public string Issuer { get; set; } = "VillageOS";

    public string Audience { get; set; } = "intake-handler";

    /// <summary>Origin(s) of the public form allowed to call this service across origins. Null leaves
    /// every cross-origin caller refused, the default a public service must start from.</summary>
    public string? PublicFormOrigin { get; set; }

    /// <summary>Time as the service reads it. A ticket is judged by how long ago it was issued, so a test
    /// that let the real clock run could only say the elapsed time was small.</summary>
    public MovableClock Clock { get; } = new(new DateTimeOffset(2026, 8, 22, 9, 30, 0, TimeSpan.Zero));

    /// <summary>What the service logged about submissions, so a test can say a line names the submission
    /// and carries none of what was submitted.</summary>
    public CapturingLogger<SubmissionIntakeService> Log { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Port", "5000");
        builder.UseSetting("MyceliumUrl", "http://localhost");
        // A non-empty token short-circuits the /api/auth/token round trip in MyceliumClientBase.
        builder.UseSetting("Token", "test-token");
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
            services.AddSingleton<IStartupFilter, ArrivingThroughTheProxy>();
        });
    }

    /// <summary>The test host opens no socket, so a request arrives with no address on it and the service
    /// reads every caller as one source. Behind the reverse proxy the connection is from loopback and the
    /// caller's own address is in the forwarded header, which is what this reproduces.</summary>
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

/// <summary>A clock a test moves by hand.</summary>
public sealed class MovableClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
