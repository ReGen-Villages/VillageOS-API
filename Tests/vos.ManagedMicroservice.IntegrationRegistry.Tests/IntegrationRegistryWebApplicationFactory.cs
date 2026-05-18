using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using vos.Tests.Shared;
using Xunit;

namespace vos.ManagedMicroservice.IntegrationRegistry.Tests;

/// <summary>
/// Custom WebApplicationFactory for IntegrationRegistry endpoint tests.
///
/// Same pattern as <c>EndpointCallerWebApplicationFactory</c> (Phase 2C / Task #5404) and
/// <c>MetabolismWebApplicationFactory</c> (Phase 2B / Task #5403), modeled on
/// <c>vos.Mycelium.Tests.BrokerWebApplicationFactory</c> from the sibling VillageOS repo,
/// including the <c>IAsyncLifetime</c> workaround for the sync-over-async deadlock in
/// <c>CreateHost</c> under the XPlat Code Coverage collector on Windows CI
/// (VillageOS Bug #5260).
///
/// Setup:
/// <list type="bullet">
///   <item>Sets <c>ASPNETCORE_ENVIRONMENT=Testing</c> so Program.cs skips Serilog file logging.</item>
///   <item>Sets <c>INTEGRATIONREGISTRY_PORT</c> + <c>INTEGRATIONREGISTRY_BROKER_URL</c> env vars.
///         CliArgs.Parse reads these as a fallback when CLI args are absent.</item>
///   <item>Writes a synthetic <c>seed.json</c> into <see cref="AppContext.BaseDirectory"/> so
///         the production <c>LoadEndpointSeed</c> helper finds it on startup. The real
///         <c>seed.json</c> is gitignored per CLAUDE.md as runtime data, so it isn't present
///         in the test process's bin folder by default. Removed in <see cref="DisposeAsync"/>.</item>
///   <item>Replaces <c>IHttpClientFactory</c> with a <c>PerCallHttpClientFactory</c> that
///         returns a fresh <c>HttpClient</c> per <c>CreateClient</c> call —
///         <c>BrokerClientBase.CreateAuthenticatedClientAsync</c> mutates <c>client.Timeout</c>
///         on every call, which throws on an already-used <c>HttpClient</c>.</item>
/// </list>
/// </summary>
public class IntegrationRegistryWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Per-test routing callback. Set BEFORE the first <c>CreateClient()</c>.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage> HandlerCallback { get; set; }
        = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    public MockHttpMessageHandler? Handler { get; private set; }

    /// <summary>Seed contents written to disk in InitializeAsync. Tests can override before init.</summary>
    public string SeedJson { get; set; } = """
    {
      "name": "Endpoint",
      "properties": {
        "url": "https://default.example/",
        "httpMethod": "GET",
        "responseTransform": "$"
      }
    }
    """;

    private string? _writtenSeedPath;

    public Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("INTEGRATIONREGISTRY_PORT", "5000");
        Environment.SetEnvironmentVariable("INTEGRATIONREGISTRY_BROKER_URL", "http://localhost");
        // BrokerClientBase.GetTokenAsync round-trip is short-circuited when a service token
        // is set; same approach as Phase 2C.
        Environment.SetEnvironmentVariable("INTEGRATIONREGISTRY_TOKEN", "test-token");

        _writtenSeedPath = Path.Combine(AppContext.BaseDirectory, "seed.json");
        File.WriteAllText(_writtenSeedPath, SeedJson);
        return Task.CompletedTask;
    }

    public new Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable("INTEGRATIONREGISTRY_PORT", null);
        Environment.SetEnvironmentVariable("INTEGRATIONREGISTRY_BROKER_URL", null);
        Environment.SetEnvironmentVariable("INTEGRATIONREGISTRY_TOKEN", null);

        if (_writtenSeedPath != null && File.Exists(_writtenSeedPath))
            File.Delete(_writtenSeedPath);

        return base.DisposeAsync().AsTask();
    }

    private sealed class PerCallHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public PerCallHttpClientFactory(HttpMessageHandler handler) { _handler = handler; }
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            Handler = new MockHttpMessageHandler(req => HandlerCallback(req));
            services.AddSingleton<IHttpClientFactory>(new PerCallHttpClientFactory(Handler));
        });
    }
}
