using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using vos.Tests.Shared;

namespace vos.Service.Intake.Tests;

// Settings reach the host through UseSetting: ServiceLaunchSettings.Parse falls back to those when no
// command-line flags are present, which is always the case under the test host. Every call the service
// makes to the model goes through the per-test handler.
public class IntakeWebApplicationFactory : WebApplicationFactory<Program>
{
    public Func<HttpRequestMessage, HttpResponseMessage> HandlerCallback { get; set; }
        = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    /// <summary>Base64 key for inbound request validation. Null leaves the service open, as it runs when no
    /// broker handed it one. Issuer and audience travel with it: the auth wireup refuses a key without
    /// them rather than falling back to a default that would reject every call.</summary>
    public string? SigningKey { get; set; }

    public string Issuer { get; set; } = "VillageOS";

    public string Audience { get; set; } = "VosClients";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Port", "5000");
        builder.UseSetting("MyceliumUrl", "http://localhost");
        // A non-empty token short-circuits the /api/auth/token round trip in MyceliumClientBase.
        builder.UseSetting("Token", "test-token");
        if (SigningKey != null)
        {
            builder.UseSetting("SigningKey", SigningKey);
            builder.UseSetting("Issuer", Issuer);
            builder.UseSetting("Audience", Audience);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(
                new PerCallHttpClientFactory(new MockHttpMessageHandler(request => HandlerCallback(request))));
        });
    }
}
