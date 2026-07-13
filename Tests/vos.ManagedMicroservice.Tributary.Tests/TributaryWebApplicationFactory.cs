using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using vos.Tests.Shared;
using Xunit;

namespace vos.ManagedMicroservice.Tributary.Tests;

// Custom WebApplicationFactory for Tributary endpoint tests.
// Pattern follows vos.Mycelium.Tests.MyceliumWebApplicationFactory from the sibling
// VillageOS repo, including the IAsyncLifetime workaround for the sync-over-async
// deadlock in CreateHost under the XPlat Code Coverage collector on Windows CI
// (VillageOS Bug #5260).
// Config is injected via UseSetting on the host builder; CliArgs.Parse reads
// these as a fallback when CLI args are absent (always the case under WebApplicationFactory).
// Tests that need a per-test signing key set SigningKey / Issuer /
// Audience on the factory instance before creating a client.
// IHttpClientFactory is replaced with one that wraps a per-test
// MockHttpMessageHandler. The handler routes BOTH mycelium calls AND the outbound
// endpoint call dispatched by CallEndpointAsync via the same factory; tests set
// HandlerCallback to control responses for their scenario.
public class TributaryWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Per-test routing callback. Defaults to returning 404 for any unhandled request so
    // tests must opt in. Set this BEFORE the first CreateClient() call.
    public Func<HttpRequestMessage, HttpResponseMessage> HandlerCallback { get; set; }
        = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    public MockHttpMessageHandler? Handler { get; private set; }

    // Base64 HMAC key for inbound-request JWT validation. Null = auth disabled.
    public string? SigningKey { get; set; }
    public string? Issuer { get; set; }
    public string? Audience { get; set; }

    public Task InitializeAsync() => Task.CompletedTask;

    public new Task DisposeAsync() => base.DisposeAsync().AsTask();

    private sealed class PerCallHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public PerCallHttpClientFactory(HttpMessageHandler handler) { _handler = handler; }
        // disposeHandler=false so the per-test MockHttpMessageHandler outlives each HttpClient.
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Port", "5000");
        builder.UseSetting("MyceliumUrl", "http://localhost");
        // Bypass MyceliumClientBase.GetTokenAsync's /api/auth/token round-trip; the test
        // MyceliumClient just needs a non-empty token to short-circuit the cache miss.
        builder.UseSetting("Token", "test-token");
        if (SigningKey != null) builder.UseSetting("SigningKey", SigningKey);
        if (Issuer != null) builder.UseSetting("Issuer", Issuer);
        if (Audience != null) builder.UseSetting("Audience", Audience);

        builder.ConfigureTestServices(services =>
        {
            // Strip the default DefaultHttpClientFactory + named-client registrations and
            // replace with one that returns a FRESH HttpClient per CreateClient call.
            // MyceliumClientBase.CreateAuthenticatedClientAsync mutates client.Timeout on every
            // call, which throws InvalidOperationException on an already-used HttpClient.
            services.RemoveAll<IHttpClientFactory>();
            Handler = new MockHttpMessageHandler(req => HandlerCallback(req));
            services.AddSingleton<IHttpClientFactory>(new PerCallHttpClientFactory(Handler));
        });
    }
}
