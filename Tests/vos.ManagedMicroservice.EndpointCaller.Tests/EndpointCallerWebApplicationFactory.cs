using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using vos.Tests.Shared;
using Xunit;

namespace vos.ManagedMicroservice.EndpointCaller.Tests;

/// <summary>
/// Custom WebApplicationFactory for EndpointCaller endpoint tests.
///
/// Pattern follows <c>MetabolismWebApplicationFactory</c> (Phase 2B / Task #5403) which in turn
/// follows <c>vos.Mycelium.Tests.BrokerWebApplicationFactory</c> from the sibling VillageOS repo,
/// including the <c>IAsyncLifetime</c> workaround for the sync-over-async deadlock in
/// <c>CreateHost</c> under the XPlat Code Coverage collector on Windows CI (VillageOS Bug #5260).
///
/// Setup:
/// <list type="bullet">
///   <item>Sets <c>ASPNETCORE_ENVIRONMENT=Testing</c> so Program.cs skips Serilog file logging.</item>
///   <item>Sets <c>ENDPOINTCALLER_PORT</c> + <c>ENDPOINTCALLER_BROKER_URL</c> env vars.
///         CliArgs.Parse reads these as a fallback when CLI args are absent — which they always
///         are under WebApplicationFactory.</item>
///   <item>Replaces <c>IHttpClientFactory</c> with one that wraps a per-test
///         <c>MockHttpMessageHandler</c>. The handler routes BOTH broker calls (FindThingByName,
///         GetEffectiveProperties, SetThingProperty, CreateThing, CreateRelationship) AND the
///         outbound endpoint call dispatched by <c>CallEndpointAsync</c> via the same factory.
///         Tests set <see cref="HandlerCallback"/> to control responses for their scenario.</item>
/// </list>
/// </summary>
public class EndpointCallerWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>
    /// Per-test routing callback. Defaults to returning 404 for any unhandled request so
    /// tests must opt in. Set this BEFORE the first <c>CreateClient()</c> call.
    /// </summary>
    public Func<HttpRequestMessage, HttpResponseMessage> HandlerCallback { get; set; }
        = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    public MockHttpMessageHandler? Handler { get; private set; }

    public Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("ENDPOINTCALLER_PORT", "5000");
        Environment.SetEnvironmentVariable("ENDPOINTCALLER_BROKER_URL", "http://localhost");
        // Bypass BrokerClientBase.GetTokenAsync's /api/auth/token round-trip; the test
        // BrokerClient just needs a non-empty token to short-circuit the cache miss.
        Environment.SetEnvironmentVariable("ENDPOINTCALLER_TOKEN", "test-token");
        return Task.CompletedTask;
    }

    public new Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable("ENDPOINTCALLER_PORT", null);
        Environment.SetEnvironmentVariable("ENDPOINTCALLER_BROKER_URL", null);
        Environment.SetEnvironmentVariable("ENDPOINTCALLER_TOKEN", null);
        return base.DisposeAsync().AsTask();
    }

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

        builder.ConfigureTestServices(services =>
        {
            // Strip the default DefaultHttpClientFactory + named-client registrations
            // and replace with one that returns a FRESH HttpClient per CreateClient call.
            // BrokerClientBase.CreateAuthenticatedClientAsync mutates client.Timeout on every
            // call, which throws InvalidOperationException on an already-used HttpClient — so
            // sharing one instance across the test's many broker calls would fail after the
            // first call.
            services.RemoveAll<IHttpClientFactory>();
            Handler = new MockHttpMessageHandler(req => HandlerCallback(req));
            services.AddSingleton<IHttpClientFactory>(new PerCallHttpClientFactory(Handler));
        });
    }
}
