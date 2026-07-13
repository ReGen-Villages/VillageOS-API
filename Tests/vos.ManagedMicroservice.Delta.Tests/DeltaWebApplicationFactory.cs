using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using vos.ManagedMicroservice.Delta.Services;
using vos.ManagedMicroservice.Delta.Tests.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.ManagedMicroservice.Delta.Tests;

// Custom WebApplicationFactory for Delta endpoint tests.
// Pattern follows vos.Mycelium.Tests.MyceliumWebApplicationFactory from the sibling
// VillageOS repo, including the IAsyncLifetime workaround for the sync-over-async
// deadlock in CreateHost under the XPlat Code Coverage collector on Windows CI
// (VillageOS Bug #5260).
// Config is injected via UseSetting on the host builder; CliArgs.Parse reads
// these as a fallback when CLI args are absent (always the case under WebApplicationFactory).
// Tests that need a per-test signing key set SigningKey / Issuer /
// Audience on the factory instance before creating a client.
// The seed is supplied in-memory: ConfigureWebHost swaps the production
// IEndpointSeedProvider (FileEndpointSeedProvider) for an
// InMemoryEndpointSeedProvider seeded from SeedJson, so each
// factory instance owns its seed without touching AppContext.BaseDirectory
// (Task #5455). Tests override SeedJson before the first CreateClient()
// to exercise malformed-seed boot paths.
// IHttpClientFactory is replaced with a PerCallHttpClientFactory that returns
// a fresh HttpClient per CreateClient call — MyceliumClientBase.CreateAuthenticatedClientAsync
// mutates client.Timeout on every call, which throws on an already-used HttpClient.
public class DeltaWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Per-test routing callback. Set BEFORE the first CreateClient().
    public Func<HttpRequestMessage, HttpResponseMessage> HandlerCallback { get; set; }
        = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    public MockHttpMessageHandler? Handler { get; private set; }

    // Base64 HMAC key for inbound-request JWT validation. Null = auth disabled.
    public string? SigningKey { get; set; }
    public string? Issuer { get; set; }
    public string? Audience { get; set; }

    // In-memory model-seed document passed to InMemoryEndpointSeedProvider at host
    // build (an vos.ManagedMicroservice.Delta.Models.EndpointSeedModel: things +
    // relationships). Tests override before the first CreateClient() to drive happy-path,
    // multi-template, malformed, and invalid-graph boot scenarios.
    public string SeedJson { get; set; } = """
    {
      "name": "Endpoint Templates",
      "things": [
        {
          "name": "Endpoint",
          "properties": {
            "url": "https://default.example/",
            "httpMethod": "GET",
            "responseTransform": "$"
          }
        }
      ],
      "relationships": []
    }
    """;

    public Task InitializeAsync() => Task.CompletedTask;

    public new Task DisposeAsync() => base.DisposeAsync().AsTask();

    private sealed class PerCallHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public PerCallHttpClientFactory(HttpMessageHandler handler) { _handler = handler; }
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Port", "5000");
        builder.UseSetting("MyceliumUrl", "http://localhost");
        builder.UseSetting("Token", "test-token");
        if (SigningKey != null) builder.UseSetting("SigningKey", SigningKey);
        if (Issuer != null) builder.UseSetting("Issuer", Issuer);
        if (Audience != null) builder.UseSetting("Audience", Audience);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            Handler = new MockHttpMessageHandler(req => HandlerCallback(req));
            services.AddSingleton<IHttpClientFactory>(new PerCallHttpClientFactory(Handler));

            services.RemoveAll<IEndpointSeedProvider>();
            services.AddSingleton<IEndpointSeedProvider>(new InMemoryEndpointSeedProvider(SeedJson));
        });
    }
}
